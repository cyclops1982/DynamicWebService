using System;
using System.Collections.Generic;
using System.Text;

using System.Reflection;
using SourceCode.SmartObjects.Services.ServiceSDK.Objects;
using SourceCode.SmartObjects.Services.ServiceSDK.Types;
using System.Web.Services.Protocols;

namespace DynamicWebService
{

    /// <summary>
    /// The helper class that works for WebServiceAccessor. Simply here to reduce the amount of code in
    /// the WebServiceAccessor.
    /// </summary>
    public static class Helper
    {
        #region Public helper methods
        /// <summary>
        /// Create a ServiceObject from a System.Reflection.MethodInfo object.
        /// 
        /// Uses the method.Name for the ServiceObject name and description.
        /// </summary>
        public static ServiceObject CreateServiceObject(MethodInfo method)
        {
            ServiceObject serviceObject = new ServiceObject();
            serviceObject.Name = method.Name;
            serviceObject.MetaData.DisplayName = method.Name;
            serviceObject.MetaData.Description = "Webservice method " + method.Name;
            serviceObject.Active = true;
            return serviceObject;
        }


        /// <summary>
        /// Create a SmartObject method for the given MethodInfo.
        /// </summary>
        /// <param name="method"></param>
        /// <returns></returns>
        public static Method CreateSmoMethod(MethodInfo method)
        {
            Method meth = new Method();

            if (method.ReturnType == typeof(void))
            {
                meth.Type = MethodType.Execute;
            }
            else if (method.ReturnType.IsArray) // this might need to be extended to support List<> etc.
            {
                meth.Type = MethodType.List;
            }
            else
            {
                meth.Type = MethodType.Read;
            }

            meth.Name = meth.Type.ToString();
            meth.MetaData.DisplayName = meth.Type.ToString();
            meth.MetaData.Description = method.Name;
            return meth;
        }



        /// <summary>
        /// Adds SmartObject properties to the given serviceObject and SmartObject method
        /// for the parameters of the method provided.
        /// 
        /// This uses the webservice assembly to check if the parameter might be a custom object.
        /// </summary>
        /// <param name="serviceObject"></param>
        /// <param name="method"></param>
        /// <param name="smoMethod"></param>
        /// <param name="webServiceAssembly"></param>
        public static void CreateInputParameters(ServiceObject serviceObject, Method smoMethod, MethodInfo method, Assembly webServiceAssembly)
        {
            ParameterInfo[] parameters = method.GetParameters();

            // We first do some checking to see if we support what we need to input.
            foreach (ParameterInfo pi in parameters)
            {
                if (pi.ParameterType.Assembly == webServiceAssembly && parameters.Length > 1)
                {
                    throw new NotSupportedException(string.Format("The method {0} has a Container Object. This is not the only parameter. We do not support this because a SmartObject is a flat object with properties.", method.Name));
                }

                if (pi.ParameterType.IsArray)
                {
                    throw new NotSupportedException(string.Format("The method {0} has an Array for it's input parameters. We do not support this because a SmartObject doesn't accept a list as one property.", method.Name));
                }
            }


            // Check if it is 1 parameter of a specific type. This is a so-called request object. If we find this
            // We use the properties of that object as input. We only support a 'flat'-container object as our SMO is flat as well.
            if (parameters.Length == 1 && parameters[0].ParameterType.Assembly == webServiceAssembly)
            {
                ParameterInfo pi = parameters[0];

                PropertyInfo[] props = pi.ParameterType.GetProperties();
                foreach (PropertyInfo prop in props)
                {
                    if (MapHelper.IsSimpleMapableType(prop.PropertyType)) // only add simple types..
                    {
                        Property property = CreateSmoProperty(prop.Name, prop.PropertyType);
                        AddServiceObjectProperty(serviceObject, property);
                        smoMethod.InputProperties.Add(property);
                    }
                    else
                    {
                        throw new NotSupportedException(string.Format("The input parameter (property) {0} of the request object is of type {1} which is not a type supported by SmartObjects.", pi.Name, pi.ParameterType.ToString()));
                    }
                }
            }
            else // It's not a request object, so we simply check all the parameters.
            {
                foreach (ParameterInfo pi in method.GetParameters())
                {
                    if (MapHelper.IsSimpleMapableType(pi.ParameterType)) // only add simple types
                    {
                        Property property = CreateSmoProperty(pi.Name, pi.ParameterType);
                        AddServiceObjectProperty(serviceObject, property);
                        smoMethod.InputProperties.Add(property);
                    }
                    else
                    {
                        throw new NotSupportedException(string.Format("The input parameter {0} is of type {1} which is not a type supported by SmartObjects.", pi.Name, pi.ParameterType.ToString()));
                    }
                }
            }

        }


        /// <summary>
        /// Add output properties to the given serviceObject and smoMethod using the given method.
        /// 
        /// The method returns directly if the smoMethod is of type Execute.
        /// 
        /// It uses the method's ReturnType to determine the properties needed.
        /// For simple types it will create one output property. If the returnType commes from within the webServiceAssembly, 
        /// it must be a container object with simple types in it. If it does not contain simple types, we cannot support it because a smartobject is a flat structure.
        /// If the returnType is an array, it may contain a container object, but again, that needs to have simple types.
        /// </summary>
        /// <param name="serviceObject"></param>
        /// <param name="method"></param>
        /// <param name="smoMethod"></param>
        /// <param name="webServiceAssembly"></param>
        public static void CreateOutputProperties(ServiceObject serviceObject, Method smoMethod, MethodInfo method, Assembly webServiceAssembly)
        {
            if (smoMethod.Type == MethodType.Execute)
            {
                return; // execute does not have return parameters!
            }


            Type returnType = method.ReturnType;

            if (MapHelper.IsSimpleMapableType(returnType)) // A simple return value
            {
                Property property = CreateSmoProperty(returnType.Name, returnType);
                AddServiceObjectProperty(serviceObject, property);
                smoMethod.ReturnProperties.Add(property);
            }
            else if (returnType.IsArray) // The return value is an array.
            {
                if (smoMethod.Type != MethodType.List)
                {
                    throw new NotSupportedException("We retrieved an array, but the method is not a list method.");
                }

                returnType = returnType.GetElementType();
                if (returnType.Assembly == webServiceAssembly) // it is an array of a container object.
                {
                    foreach (PropertyInfo prop in returnType.GetProperties())
                    {
                        if (!MapHelper.IsSimpleMapableType(prop.PropertyType))
                        {
                            throw new NotSupportedException("The return type of the web service is a Container Object inside an Array.The Container Object contains a non-simple type which we cannot support.");
                        }

                        Property property = CreateSmoProperty(prop.Name, prop.PropertyType);
                        AddServiceObjectProperty(serviceObject, property);
                        smoMethod.ReturnProperties.Add(property);
                    }
                }
                else if (MapHelper.IsSimpleMapableType(returnType))
                {
                    Property property = CreateSmoProperty(returnType.Name, returnType);
                    AddServiceObjectProperty(serviceObject, property);
                    smoMethod.ReturnProperties.Add(property);
                }
                else
                {
                    throw new NotSupportedException(string.Format("Return type is an array of element {0}. Which is not supported.", returnType.ToString()));
                }
            }
            else if (returnType.Assembly == webServiceAssembly) // Not a simple type, not an array, it must be a complex object.
            {
                foreach (PropertyInfo prop in returnType.GetProperties())
                {
                    if (!MapHelper.IsSimpleMapableType(prop.PropertyType))
                    {
                        throw new NotSupportedException("The return type of the web service is a Container Object or an Array. But it contains non simple types.");
                    }

                    Property property = CreateSmoProperty(prop.Name, prop.PropertyType);
                    AddServiceObjectProperty(serviceObject, property);
                    smoMethod.ReturnProperties.Add(property);
                }
            }
            else
            {
                throw new NotSupportedException("Could not create output properties. The returntype is not a Simple type, an array or not a simple type....");
            }
        }

        /// <summary>
        /// Simple check to see if the Soap Method is supported.
        /// 
        /// This does not check if the types are correct or so, it simply checks if this is a 
        /// functional method of the webservice. The proxy class has more methods than we need that 
        /// are "out of the box" for soap.
        /// 
        /// This method also should allow us to have support for Soap1 and Soap1.2 becaues the method attributes
        /// are different for those versions.
        /// </summary>
        /// <param name="method"></param>
        /// <returns></returns>
        public static bool IsSupportedSoapMethod(MethodInfo method)
        {
            if (method.GetCustomAttributes(typeof(SoapRpcMethodAttribute), false).Length > 0 ||
                method.GetCustomAttributes(typeof(SoapDocumentMethodAttribute), false).Length > 0)
            {
                return true;
            }
            return false;
        }
        #endregion Public helper methods

        #region Private helper methods
        private static Property CreateSmoProperty(string name, Type type)
        {
            Property property = new Property();
            property.Name = name;
            property.MetaData.DisplayName = name;

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                property.Type = Nullable.GetUnderlyingType(type).ToString();
            }
            property.Type = type.ToString();
            property.SoType = MapHelper.GetSoTypeByType(type);
            return property;
        }


        private static void AddServiceObjectProperty(ServiceObject serviceObject, Property property)
        {
            if (!serviceObject.Properties.Contains(property.Name))
            {
                serviceObject.Properties.Add(property);
            }
            else
            {
                // We only support one web service with unique parameters. This could actually be different if we prefix the property with a method name.
                if (serviceObject.Properties[property.Name].SoType != property.SoType)
                {
                    throw new Exception("Custom object contains property with the same name as one of the parameters to the web service, but which is of a different type.");
                }
            }
        }

        #endregion private helper methods
    }
}