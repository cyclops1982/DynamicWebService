using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Net;
using System.Web.Services;
using System.Web.Services.Description;
using System.Web.Services.Discovery;
using System.CodeDom;
using System.CodeDom.Compiler;
using System.Xml;
using System.Reflection;
using System.Web.Services.Protocols;
using System.Collections;
using System.Data;
using SourceCode.SmartObjects.Services.ServiceSDK.Objects;
using SourceCode.SmartObjects.Services.ServiceSDK.Types;
using SourceCode.SmartObjects.Services.ServiceSDK;
using System.Reflection.Emit;

namespace DynamicWebService
{
    /// <summary>
    /// Object responsible for the actual work that the ServiceBroker does.
    /// </summary>
    public class WebServiceAccessor
    {
        #region Private Fields
        private ServiceAssemblyBase _serviceBroker = null;
        private static Dictionary<string, Assembly> _loadedServiceProxies = new Dictionary<string, Assembly>();
        #endregion Private Fields

        #region Constructor
        public WebServiceAccessor(ServiceAssemblyBase broker)
        {
            _serviceBroker = broker;
        }
        #endregion Constructor

        #region Private Properties

        private string WebServiceUrl
        {
            get
            {
                return _serviceBroker.Service.ServiceConfiguration["URL"].ToString();
            }
        }

        private bool SkipUnsupportedMethods
        {
            get
            {
                return bool.Parse(_serviceBroker.Service.ServiceConfiguration["Skip unsupported methods"].ToString());
            }
        }

        private int WebServiceTimeout
        {
            get
            {
                int timeout = 30;
                string confOption =_serviceBroker.Service.ServiceConfiguration["Timeout"].ToString();
                if (string.IsNullOrEmpty(confOption))
                {
                    return (timeout * 1000);
                }
                int.TryParse(confOption, out timeout);
                return (timeout * 1000);
            }
        }


        private bool UseImpersonation
        {
            get
            {
                return _serviceBroker.Service.ServiceConfiguration.ServiceAuthentication.Impersonate;
            }
        }


        private ICredentials CredentialsForServiceCall
        {
            get
            {
                //TODO: Add code to do service impersonation.
                switch (_serviceBroker.Service.ServiceConfiguration.ServiceAuthentication.AuthenticationMode)
                {
                    case AuthenticationMode.Static:
                        string username = _serviceBroker.Service.ServiceConfiguration.ServiceAuthentication.UserName;
                        string password = _serviceBroker.Service.ServiceConfiguration.ServiceAuthentication.Password;
                        string domain = _serviceBroker.Service.ServiceConfiguration.ServiceAuthentication.Extra;
                        return new NetworkCredential(username, password, domain);
                    case AuthenticationMode.Impersonate:
                    default:
                        return CredentialCache.DefaultNetworkCredentials;
                }
            }
        }


        private Assembly WebServiceAssembly
        {
            get
            {
                if (!_loadedServiceProxies.ContainsKey(WebServiceUrl))
                {
                    // We specify a path, because we like to have it in a 'k2 folder' somewhere.
                    string brokerGuid = Guid.NewGuid().ToString("N");
                    string datetime = DateTime.Now.ToString("yyyyMMdd-hhmmss");
                    string path = string.Format("{0}\\DWS_{1}_{2}.dll", Environment.CurrentDirectory, datetime, brokerGuid);

                    Assembly asm = GenerateProxy(WebServiceUrl, path);
                    _loadedServiceProxies.Add(WebServiceUrl, asm);
                }
                return _loadedServiceProxies[WebServiceUrl];
            }
        }
        #endregion Private Properties

        #region Public Methods
        /// <summary>
        /// Describes the schema for this webservice. Uses the generated proxy class and 
        /// reflection to create SmartObject Methods and SmartObject parameters.
        /// </summary>
        public void DescribeSchema()
        {
            // General properties
            _serviceBroker.Service.Name = "DynamicWebService";
            _serviceBroker.Service.MetaData.DisplayName = String.Format("Dynamic Web Service: {0}", WebServiceUrl);
            _serviceBroker.Service.MetaData.Description = "Provides K2 [blackpearl] with the capabilities required to interface with web service.";


            // Clean out the cache we have.
            _loadedServiceProxies.Remove(WebServiceUrl);

            Type webServiceType = WebServiceAssembly.GetTypes()[0];
            TypeMappings map = MapHelper.Map;

            try
            {
                //Foreach method in our webservice...
                foreach (MethodInfo method in webServiceType.GetMethods())
                {
                    if (Helper.IsSupportedSoapMethod(method))
                    {
                       AddServiceObject(method, SkipUnsupportedMethods);
                        
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception(string.Format("Failed to create web service proxy class: {0}", ex.Message), ex);
            }
        }


        /// <summary>
        /// Executes the webservice call and fills the result table.
        /// </summary>
        public void Execute()
        {
            ServiceObject serviceObject = _serviceBroker.Service.ServiceObjects[0];
            Method smoMethod = serviceObject.Methods[0];

            Type wsType = WebServiceAssembly.GetTypes()[0];
            SoapHttpClientProtocol wsInstance = (SoapHttpClientProtocol)WebServiceAssembly.CreateInstance(wsType.Name);
            wsInstance.Credentials = CredentialsForServiceCall;
            wsInstance.Timeout = WebServiceTimeout;

            MethodInfo wsMethod = wsType.GetMethod(serviceObject.Name);

            // Create some parameters
            object[] wsMethodParameters = CreateWebServiceInputParameters(wsMethod);

            // Invoke web service
            object returnValue = wsMethod.Invoke(wsInstance, wsMethodParameters);

            FillResultTable(wsMethod.ReturnType, returnValue);
        }
        #endregion Public Methods

        #region Private Helper methods
        /// <summary>
        /// Generates a web service proxy.
        /// Code based on the code in this post: 
        /// http://blogs.msdn.com/kaevans/archive/2006/04/27/585013.aspx
        /// </summary>
        /// <param name="webServiceUrl"></param>
        private Assembly GenerateProxy(string url, string path)
        {
            WebClient client = new WebClient();
            client.Credentials = CredentialsForServiceCall;

            //Does the url already have ?wsdl on the end? If not, let's add it.
            if (!url.ToLower().EndsWith("?wsdl"))
            {
                url += "?wsdl";
            }

            //Read in the WSDL
            Stream stream = client.OpenRead(url);
            ServiceDescription description = ServiceDescription.Read(stream);

            // Use the ServiceDescriptionImporter to specify what we wnat to generate.
            ServiceDescriptionImporter importer = new ServiceDescriptionImporter();
            importer.AddServiceDescription(description, null, null);
            importer.Style = ServiceDescriptionImportStyle.Client;
            importer.CodeGenerationOptions = System.Xml.Serialization.CodeGenerationOptions.GenerateProperties;

            // Initialize a Code-DOM tree
            CodeNamespace codeNamespace = new CodeNamespace();
            CodeCompileUnit compileUnit = new CodeCompileUnit();
            compileUnit.Namespaces.Add(codeNamespace);

            // Import the service into the Code-DOM tree. This creates proxy code that uses the service.
            ServiceDescriptionImportWarnings warning = importer.Import(codeNamespace, compileUnit);

            if (warning == 0) // Check if WSDL was imported correctly
            {
                CompilerParameters parms = new CompilerParameters();
                parms.ReferencedAssemblies.Add("System.Web.Services.dll");
                parms.ReferencedAssemblies.Add("System.Xml.dll");
                parms.CompilerOptions = "/optimize";
                parms.OutputAssembly = path;

                CodeDomProvider codeProvider = CodeDomProvider.CreateProvider("CSharp");
                CompilerResults compileResult = codeProvider.CompileAssemblyFromDom(parms, compileUnit);

                if (compileResult.Errors.Count > 0) // check compile errors and generate a nice error message.
                {
                    StringBuilder compileErrors = new StringBuilder();
                    foreach (CompilerError compileError in compileResult.Errors)
                    {
                        compileErrors.AppendFormat("Compile Error: {0} - ", compileError.ErrorNumber, compileError.ErrorText);
                    }
                    throw new Exception(compileErrors.ToString());
                }
                return compileResult.CompiledAssembly;
            }
            throw new Exception("Warnings encountered creating service.");
        }


        private void AddServiceObject(MethodInfo method, bool skipUnsupportedMethods)
        {
            ServiceObject serviceObject = Helper.CreateServiceObject(method);

            Method smoMethod = Helper.CreateSmoMethod(method);
            try
            {
                // Create output and input properties based on the web service method
                Helper.CreateInputParameters(serviceObject, smoMethod, method, WebServiceAssembly);
                Helper.CreateOutputProperties(serviceObject, smoMethod, method, WebServiceAssembly);
            }
            catch (NotSupportedException ex)
            {
                if (! skipUnsupportedMethods)
                {
                    throw ex;
                }
                return;
            }
            catch (Exception ex)
            {
                throw ex;
            }

            //Add the method to the service object
            serviceObject.Methods.Add(smoMethod);

            // Add the serviceObject to the service broker.
            _serviceBroker.Service.ServiceObjects.Add(serviceObject);
        }



        private void FillResultTable(Type returnType, object returnValue)
        {
            ServiceObject serviceObject = _serviceBroker.Service.ServiceObjects[0];
            Method smoMethod = serviceObject.Methods[0];

            serviceObject.Properties.InitResultTable();
            DataTable results = _serviceBroker.ServicePackage.ResultTable;

            if (returnValue == null) // A webservice might return nothing, something execute could do, but if it DID return something, the value can still be NULL.
            {
                return;
            }

            if (smoMethod.Type == MethodType.Read)
            {
                DataRow row = results.NewRow();

                if (MapHelper.IsSimpleMapableType(returnType))
                {
                    Property smoProp = serviceObject.Properties[smoMethod.ReturnProperties[0]];
                    row[smoProp.Name] = returnValue;
                }
                else
                {
                    foreach (string returnPropName in smoMethod.ReturnProperties)
                    {
                        Property smoProp = serviceObject.Properties[returnPropName];
                        PropertyInfo propInfo = returnType.GetProperty(returnPropName);
                        object val = propInfo.GetValue(returnValue, null);
                        row[smoProp.Name] = val;
                    }
                }
                results.Rows.Add(row);
            }
            else if (smoMethod.Type == MethodType.List)
            {
                foreach (object obj in ((IEnumerable)returnValue))
                {
                    //We know we are dealing with an array, but is it an array of simple types (value objects,
                    //strings, etc) or is it an array of custom objects?
                    if (MapHelper.IsSimpleMapableType(obj.GetType()))
                    {
                        //If it is a simple type then we can just assign the value to our return values
                        results.Rows.Add(obj);
                    }
                    else //it is a complex object, so let's use reflection to return the properties
                    {
                        DataRow row = results.NewRow();
                        foreach (string returnPropName in smoMethod.ReturnProperties)
                        {
                            Property smoProp = serviceObject.Properties[returnPropName];
                            row[smoProp.Name] = obj.GetType().GetProperty(returnPropName).GetValue(obj, null);
                        }
                        results.Rows.Add(row);
                    }
                }
            }
        }

        private object[] CreateWebServiceInputParameters(MethodInfo wsMethod)
        {
            ServiceObject serviceObject = _serviceBroker.Service.ServiceObjects[0];
            Method smoMethod = serviceObject.Methods[0];


            ParameterInfo[] parameters = wsMethod.GetParameters();
            object[] wsMethodParameters = new object[parameters.Length];
            int inputPropCount = 0;
            if (parameters.Length == 1 && parameters[0].ParameterType.Assembly == WebServiceAssembly)
            {
                object inputObject = Activator.CreateInstance(parameters[0].ParameterType);
                foreach (string inputPropName in smoMethod.InputProperties)
                {
                    Property inputProp = serviceObject.Properties[inputPropName];
                    PropertyInfo propInfo = parameters[0].ParameterType.GetProperty(inputProp.Name);
                    propInfo.SetValue(inputObject, Convert.ChangeType(inputProp.Value, Type.GetType(inputProp.Type)), null);
                }
                wsMethodParameters[inputPropCount] = inputObject;
            }
            else
            {

                // Create input parameters for the Web Service, value comes from the ServiceObject Property value
                foreach (string inputPropName in smoMethod.InputProperties)
                {
                    Property inputProp = serviceObject.Properties[inputPropName];
                    wsMethodParameters[inputPropCount] = Convert.ChangeType(inputProp.Value, Type.GetType(inputProp.Type));
                    inputPropCount++;
                }

            }
            return wsMethodParameters;
        }
        #endregion Private Helper methods
    }
}
