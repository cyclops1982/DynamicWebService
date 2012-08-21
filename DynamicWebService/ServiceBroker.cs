using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Transactions;
using SourceCode.SmartObjects.Services.ServiceSDK;
using SourceCode.SmartObjects.Services.ServiceSDK.Objects;
using SourceCode.SmartObjects.Services.ServiceSDK.Types;

namespace DynamicWebService
{

    /// <summary>
    /// ServiceBroker to handle and override the methods from ServiceAssemblybase.
    /// Mostly does exception handling to surface a nice message to k2.
    /// </summary>
    public class ServiceBroker : ServiceAssemblyBase
    {
        #region Private Fields
        private WebServiceAccessor accessor = null;
        #endregion Private Fields

        #region Constructor
        public ServiceBroker()
        {
            accessor = new WebServiceAccessor(this);
        }
        #endregion Constructor

        #region Public overriden methods
        public override string DescribeSchema()
        {
            try
            {
                accessor.DescribeSchema();
                ServicePackage.IsSuccessful = true;
            }
            catch (Exception ex)
            {
                StringBuilder error = new StringBuilder();
                error.AppendFormat("Exception: {0}", ex.Message);

                if (ex.InnerException != null)
                {
                    error.AppendFormat("InnerException: {0}", ex.InnerException.Message);
                }
                ServicePackage.ServiceMessages.Add(error.ToString(), MessageSeverity.Error);
                ServicePackage.IsSuccessful = false;
            }

            return base.DescribeSchema();
        }

        public override string GetConfigSection()
        {
            this.Service.ServiceConfiguration.Add(Constants.Config.WebServiceUrl, true, string.Empty);
            this.Service.ServiceConfiguration.Add(Constants.Config.WebServiceTimeout, true, Constants.Config.DefaultWebServiceTimeout);
            this.Service.ServiceConfiguration.Add(Constants.Config.WebServiceDynamicUrl, true, false);
            this.Service.ServiceConfiguration.Add(Constants.Config.SkipUnsupportedMethods, false, true);
            return base.GetConfigSection();
        }

        public override void Execute()
        {
            try
            {
                accessor.Execute();
                ServicePackage.IsSuccessful = true;
            }
            catch (Exception ex)
            {
                StringBuilder error = new StringBuilder();
                error.AppendFormat("Exception.Message: {0}", ex.Message);
                error.AppendFormat("Exception.StackTrace: {0}", ex.Message);

                Exception innerEx = ex;
                int i = 0;
                while (innerEx.InnerException != null)
                {
                    error.AppendFormat("{0} InnerException.Message: {1}", i, ex.InnerException.Message);
                    error.AppendFormat("{0} InnerException.StackTrace: {1}", i, ex.InnerException.StackTrace);
                    innerEx = innerEx.InnerException;
                    i++;
                }
                ServicePackage.ServiceMessages.Add(error.ToString(), MessageSeverity.Error);
                ServicePackage.IsSuccessful = false;
            }
        }


        /// <summary>
        /// Not implemented, but need to be overriden.
        /// </summary>
        public override void Extend()
        {
        }
        #endregion Public overriden methods
    }
}
