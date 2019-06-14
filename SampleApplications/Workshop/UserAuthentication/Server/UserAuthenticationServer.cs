/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * 
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 * 
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.IdentityModel.Selectors;
using System.IdentityModel.Tokens;
using System.Security.Principal;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml;
using System.Runtime.InteropServices;
using Opc.Ua;
using Opc.Ua.Server;
using System.IdentityModel.Tokens.Jwt;

namespace Quickstarts.UserAuthenticationServer
{
    /// <summary>
    /// Implements a basic Quickstart Server.
    /// </summary>
    /// <remarks>
    /// Each server instance must have one instance of a StandardServer object which is
    /// responsible for reading the configuration file, creating the endpoints and dispatching
    /// incoming requests to the appropriate handler.
    /// 
    /// This sub-class specifies non-configurable metadata such as Product Name and initializes
    /// the UserAuthenticationNodeManager which provides access to the data exposed by the Server.
    /// </remarks>
    public partial class UserAuthenticationServer : StandardServer
    {
        #region Overridden UserAuthentication
        /// <summary>
        /// Initializes the server before it starts up.
        /// </summary>
        /// <remarks>
        /// This method is called before any startup processing occurs. The sub-class may update the 
        /// configuration object or do any other application specific startup tasks.
        /// </remarks>
        protected override void OnServerStarting(ApplicationConfiguration configuration)
        {
            Console.WriteLine("The Server is starting.");

            base.OnServerStarting(configuration);

            // it is up to the application to decide how to validate user identity tokens.
            // this function creates validators for identity tokens.
            CreateUserIdentityValidators(configuration);
        }

        /// <summary>
        /// Called after the server has been started.
        /// </summary>
        protected override void OnServerStarted(IServerInternal server)
        {
            base.OnServerStarted(server);

            // request notifications when the user identity is changed. all valid users are accepted by default.
            server.SessionManager.ImpersonateUser += new ImpersonateEventHandler(SessionManager_ImpersonateUser);
        }

        /// <summary>
        /// Creates the node managers for the server.
        /// </summary>
        /// <remarks>
        /// This method allows the sub-class create any additional node managers which it uses. The SDK
        /// always creates a CoreNodeManager which handles the built-in nodes defined by the specification.
        /// Any additional NodeManagers are expected to handle application specific nodes.
        /// </remarks>
        protected override MasterNodeManager CreateMasterNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        {
            Utils.Trace("Creating the Node Managers.");

            List<INodeManager> nodeManagers = new List<INodeManager>();

            // create the custom node managers.
            nodeManagers.Add(new UserAuthenticationNodeManager(server, configuration));
            
            // create master node manager.
            return new MasterNodeManager(server, configuration, null, nodeManagers.ToArray());
        }

        /// <summary>
        /// Loads the non-configurable properties for the application.
        /// </summary>
        /// <remarks>
        /// These properties are exposed by the server but cannot be changed by administrators.
        /// </remarks>
        protected override ServerProperties LoadServerProperties()
        {
            ServerProperties properties = new ServerProperties();

            properties.ManufacturerName = "OPC Foundation";
            properties.ProductName      = "OPC UA Quickstarts";
            properties.ProductUri       = "http://opcfoundation.org/Quickstarts/UserAuthenticationServer/v1.0";
            properties.SoftwareVersion  = Utils.GetAssemblySoftwareVersion();
            properties.BuildNumber      = Utils.GetAssemblyBuildNumber();
            properties.BuildDate        = Utils.GetAssemblyTimestamp();

            // TBD - All applications have software certificates that need to added to the properties.

            return properties;
        }

        /// <summary>
        /// Creates the resource manager for the server.
        /// </summary>
        protected override ResourceManager CreateResourceManager(IServerInternal server, ApplicationConfiguration configuration)
        {
            ResourceManager resourceManager = new ResourceManager(server, configuration);

            // add some localized strings to the resource manager to demonstrate that localization occurs.
            resourceManager.Add("InvalidPassword", "de-DE", "Das Passwort ist nicht gültig für Konto '{0}'.");
            resourceManager.Add("InvalidPassword", "es-ES", "La contraseña no es válida para la cuenta de '{0}'.");

            resourceManager.Add("UnexpectedUserTokenError", "fr-FR", "Une erreur inattendue s'est produite lors de la validation utilisateur.");
            resourceManager.Add("UnexpectedUserTokenError", "de-DE", "Ein unerwarteter Fehler ist aufgetreten während des Anwenders.");

            resourceManager.Add("BadUserAccessDenied", "fr-FR", "Utilisateur ne peut pas changer la valeur.");
            resourceManager.Add("BadUserAccessDenied", "de-DE", "User nicht ändern können, Wert.");
                        
            return resourceManager;
        }

        /// <summary>
        /// Creates the objects used to validate the user identity tokens supported by the server.
        /// </summary>
        private void CreateUserIdentityValidators(ApplicationConfiguration configuration)
        {
            // create the validator for X509 identity tokens.
            for (int ii = 0; ii < configuration.ServerConfiguration.UserTokenPolicies.Count; ii++)
            {
                UserTokenPolicy policy = configuration.ServerConfiguration.UserTokenPolicies[ii];

                // create a validator for a certificate token policy.
                if (policy.TokenType == UserTokenType.Certificate)
                {
                    // the name of the element in the configuration file.
                    XmlQualifiedName qname = new XmlQualifiedName(policy.PolicyId, Opc.Ua.Namespaces.OpcUa);

                    // find the location of the trusted issuers.
                    CertificateTrustList trustedIssuers = configuration.ParseExtension<CertificateTrustList>(qname);

                    if (trustedIssuers == null)
                    {
                        Utils.Trace(
                            (int)Utils.TraceMasks.Error,
                            "Could not load CertificateTrustList for UserTokenPolicy {0}",
                            policy.PolicyId);

                        continue;
                    }

                    // trusts any certificate in the trusted people store.
                    m_certificateValidator = X509CertificateValidator.PeerTrust;
                }
            }

            // create the validators for JWT tokens.
            m_validators = configuration.ParseExtension<UserTokenValidators>();

            if (m_validators != null)
            {
                foreach (var validator in m_validators)
                {
                    var hostname = System.Net.Dns.GetHostName().ToLower();

                    if (validator.AuthorityCertificate.SubjectName != null)
                    {
                        validator.AuthorityCertificate.SubjectName = validator.AuthorityCertificate.SubjectName.Replace("localhost", hostname);
                    }

                    var certificate = validator.AuthorityCertificate.Find(false).Result;

                    if (certificate == null)
                    {
                        Utils.Trace("UserTokenValidators Certificate could not be found: {0}", certificate.SubjectName);
                    }

                    if (validator.IssuerUri == null)
                    {
                        validator.IssuerUri = Utils.GetApplicationUriFromCertificate(certificate);
                    }
                    else
                    {
                        validator.IssuerUri = validator.IssuerUri.Replace("localhost", hostname);
                    }

                    foreach (var policy in configuration.ServerConfiguration.UserTokenPolicies)
                    {
                        if (policy.PolicyId == validator.PolicyId)
                        {
                            policy.IssuerEndpointUrl = validator.IssuerEndpointUrl;
                            break;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Called when a client tries to change its user identity.
        /// </summary>
        private void SessionManager_ImpersonateUser(Session session, ImpersonateEventArgs args)
        {
            // check for an issued token.
            IssuedIdentityToken issuedToken = args.NewIdentity as IssuedIdentityToken;

            if (issuedToken != null)
            {
                if (args.UserTokenPolicy.IssuedTokenType == Opc.Ua.Profiles.JwtUserToken)
                {
                    JwtEndpointParameters parameters = new JwtEndpointParameters();
                    parameters.FromJson(args.UserTokenPolicy.IssuerEndpointUrl);
                    var jwt = new UTF8Encoding().GetString(issuedToken.DecryptedTokenData);
                    var identity = ValidateJwt(args.UserTokenPolicy, parameters, jwt);
                    Utils.Trace("JSON Web Token Accepted: {0}", identity.DisplayName);
                    args.Identity = identity;
                    return;
                }
            }

            // check for a user name token.
            UserNameIdentityToken userNameToken = args.NewIdentity as UserNameIdentityToken;

            if (userNameToken != null)
            {
                VerifyPassword(userNameToken.UserName, userNameToken.DecryptedPassword);
                args.Identity = new UserIdentity(userNameToken);
                Utils.Trace("UserName Token Accepted: {0}", args.Identity.DisplayName);
                return;
            }

            // check for x509 user token.
            X509IdentityToken x509Token = args.NewIdentity as X509IdentityToken;

            if (x509Token != null)
            {
                VerifyCertificate(x509Token.Certificate);
                args.Identity = new UserIdentity(x509Token);
                Utils.Trace("X509 Token Accepted: {0}", args.Identity.DisplayName);
                return;
            }
        }

        /// <summary>
        /// Validates the password for a username token.
        /// </summary>
        private void VerifyPassword(string userName, string password)
        {
            // accept user an password for authorization service
            if (userName == "authorizationService" && password == "appadmin")
            {
                return;
            }

            IntPtr handle = IntPtr.Zero;

            const int LOGON32_PROVIDER_DEFAULT = 0;
            // const int LOGON32_LOGON_INTERACTIVE = 2;
            const int LOGON32_LOGON_NETWORK = 3;
            // const int LOGON32_LOGON_BATCH = 4;

            bool result = NativeMethods.LogonUser(
                userName,
                String.Empty,
                password,
                LOGON32_LOGON_NETWORK,
                LOGON32_PROVIDER_DEFAULT,
                ref handle);

            if (!result)
            {
                throw ServiceResultException.Create(StatusCodes.BadUserAccessDenied, "Login failed for user: {0}", userName);
            }

            NativeMethods.CloseHandle(handle);
        }

        /// <summary>
        /// Verifies that a certificate user token is trusted.
        /// </summary>
        private void VerifyCertificate(X509Certificate2 certificate)
        {
            try
            {
                m_certificateValidator.Validate(certificate);
            }
            catch (Exception e)
            {
                // construct translation object with default text.
                TranslationInfo info = new TranslationInfo(
                    "InvalidCertificate",
                    "en-US",
                    "'{0}' is not a trusted user certificate.",
                    certificate.Subject);

                // create an exception with a vendor defined sub-code.
                throw new ServiceResultException(new ServiceResult(
                    e,
                    StatusCodes.BadIdentityTokenRejected,
                    "InvalidCertificate",
                    Namespaces.UserAuthentication,
                    new LocalizedText(info)));
            }
        }

        private IUserIdentity ValidateJwt(UserTokenPolicy policy, JwtEndpointParameters parameters, string jwt)
        {
            string issuerUri = null;
            X509Certificate2 authorityCertificate = null;

            if (m_validators != null)
            {
                foreach (var validator in m_validators)
                {
                    if (validator.PolicyId == policy.PolicyId)
                    {
                        authorityCertificate = validator.AuthorityCertificate.Find(false).Result;
                        issuerUri = validator.IssuerUri;
                        break;
                    }
                }
            }

            IUserIdentity identity = JwtUtils.ValidateToken(new Uri(parameters.AuthorityUrl), authorityCertificate, issuerUri, Configuration.ApplicationUri, jwt);

            JwtSecurityToken jwtToken = identity.GetSecurityToken() as JwtSecurityToken;

            if (jwtToken == null)
            {
                throw new ServiceResultException(StatusCodes.BadInternalError);
            }

            return identity;
        }
        #endregion

        private static class NativeMethods
        {
            [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            public static extern bool LogonUser(
                string lpszUsername, 
                string lpszDomain, 
                string lpszPassword,
                int dwLogonType, 
                int dwLogonProvider, 
                ref IntPtr phToken);

            [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
            public extern static bool CloseHandle(IntPtr handle);
        }

        private class ImpersonationContext : IDisposable
        {
            public WindowsImpersonationContext Context;
            public IntPtr Handle;
        
        #region IDisposable Members
            /// <summary>
            /// The finializer implementation.
            /// </summary>
            ~ImpersonationContext() 
            {
                Dispose(false);
            }
            
            /// <summary>
            /// Frees any unmanaged resources.
            /// </summary>
            public void Dispose()
            {   
                Dispose(true);
                GC.SuppressFinalize(this);
            }

            /// <summary>
            /// An overrideable version of the Dispose.
            /// </summary>
            protected virtual void Dispose(bool disposing)
            {
                if (disposing) 
                {
                    Utils.SilentDispose(Context);
                }

                if (Handle != IntPtr.Zero)
                {
                    NativeMethods.CloseHandle(Handle);
                    Handle = IntPtr.Zero;
                }
            }
        #endregion
        }

        // need to ensure the contexts are undone.
        private Dictionary<uint, ImpersonationContext> m_contexts = new Dictionary<uint, ImpersonationContext>();
        
        /// <summary>
        /// Impersonates the windows user identifed by the security token.
        /// </summary>
        private void LogonUser(OperationContext context, UserNameSecurityToken securityToken)
        {
            IntPtr handle = IntPtr.Zero;

            const int LOGON32_PROVIDER_DEFAULT = 0;
            // const int LOGON32_LOGON_INTERACTIVE = 2;
            const int LOGON32_LOGON_NETWORK = 3;
            // const int LOGON32_LOGON_BATCH = 4;

            bool result = NativeMethods.LogonUser(
                securityToken.UserName, 
                String.Empty, 
                securityToken.Password,
                LOGON32_LOGON_NETWORK, 
                LOGON32_PROVIDER_DEFAULT,
                ref handle);

            if (!result)
            {
                throw ServiceResultException.Create(StatusCodes.BadUserAccessDenied, "Login failed for user: {0}", securityToken.UserName);
            }

            WindowsIdentity identity = new WindowsIdentity(handle);

            ImpersonationContext impersonationContext = new ImpersonationContext();
            impersonationContext.Handle = handle;
            impersonationContext.Context = identity.Impersonate();

            lock (this.m_lock)
            {
                m_contexts.Add(context.RequestId, impersonationContext);
            }
        }

        /// <summary>
        /// This method is called at the being of the thread that processes a request.
        /// </summary>
        protected override OperationContext ValidateRequest(RequestHeader requestHeader, RequestType requestType)
        {
            OperationContext context = base.ValidateRequest(requestHeader, requestType);

            if (requestType == RequestType.Write)
            {
                // reject all writes if no user provided.
                if (context.UserIdentity.TokenType == UserTokenType.Anonymous)
                {
                    // construct translation object with default text.
                    TranslationInfo info = new TranslationInfo(
                        "NoWriteAllowed",
                        "en-US",
                        "Must provide a valid windows user before calling write.");

                    // create an exception with a vendor defined sub-code.
                    throw new ServiceResultException(new ServiceResult(
                        StatusCodes.BadUserAccessDenied,
                        "NoWriteAllowed",
                        Namespaces.UserAuthentication,
                        new LocalizedText(info)));
                }
#if TODO
                SecurityToken securityToken = context.UserIdentity.GetSecurityToken();

                // check for a kerberso token.
                KerberosReceiverSecurityToken kerberosToken = securityToken as KerberosReceiverSecurityToken;

                if (kerberosToken != null)
                {
                    ImpersonationContext impersonationContext = new ImpersonationContext();
                    impersonationContext.Context = kerberosToken.WindowsIdentity.Impersonate();

                    lock (this.m_lock)
                    {
                        m_contexts.Add(context.RequestId, impersonationContext);
                    }
                }

                // check for a user name token.
                UserNameSecurityToken userNameToken = securityToken as UserNameSecurityToken;

                if (userNameToken != null)
                {
                    LogonUser(context, userNameToken);
                }
#endif
            }

            return context;
        }

        /// <summary>
        /// This method is called in a finally block at the end of request processing (i.e. called even on exception).
        /// </summary>
        protected override void OnRequestComplete(OperationContext context)
        {
           ImpersonationContext impersonationContext = null;

            lock (this.m_lock)
            {
                if (m_contexts.TryGetValue(context.RequestId, out impersonationContext))
                {
                    m_contexts.Remove(context.RequestId);
                }
            }

            if (impersonationContext != null)
            {
                impersonationContext.Context.Undo();
                impersonationContext.Dispose();
            }

            base.OnRequestComplete(context);
        }

        #region Private Fields
        private object m_lock = new object();
        private X509CertificateValidator m_certificateValidator;
        private UserTokenValidators m_validators;
        #endregion
    }
}
