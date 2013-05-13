using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Text;

namespace RuralCafe
{
    /// <summary>
    /// Superclass for local and remote internal handlers.
    /// </summary>
    public abstract class InternalRequestHandler : RequestHandler
    {
        /// <summary>
        /// A method used to process a called routine.
        /// </summary>
        protected class RoutineMethod
        {
            /// <summary>
            /// A routine without parameters.
            /// </summary>
            /// <param name="methodName"></param>
            public RoutineMethod(String methodName)
            {
                this._methodName = methodName;
            }

            /// <summary>
            /// A routine with parameters.
            /// </summary>
            /// <param name="methodName">The method name.</param>
            /// <param name="parameterNames">The names of the parameters in the URI.</param>
            /// <param name="parameterTypes">The types of the parameters.</param>
            public RoutineMethod(String methodName, String[] parameterNames,
                Type[] parameterTypes)
            {
                this._methodName = methodName;
                this._parameterNames = parameterNames;
                this._parameterTypes = parameterTypes;
            }

            private String _methodName;
            private String[] _parameterNames;
            private Type[] _parameterTypes;

            public String MethodName
            {
                get { return _methodName; }
            }
            public String[] ParameterNames
            {
                get { return _parameterNames; }
            }
            public Type[] ParameterTypes
            {
                get { return _parameterTypes; }
            }
        }

        /// <summary>
        /// All routines of this handler.
        /// </summary>
        protected Dictionary<String, RoutineMethod> _routines;
        /// <summary>
        /// The default method.
        /// </summary>
        protected RoutineMethod _defaultMethod;

        /// <summary>
        /// Constructor for a internal proxy's request handler.
        /// </summary>
        /// <param name="proxy">Proxy this request handler belongs to.</param>
        /// <param name="socket">Client socket.</param>
        protected InternalRequestHandler(RCLocalProxy proxy, Socket socket, 
            Dictionary<String, RoutineMethod> routines, RoutineMethod defaultMethod)
            : base(proxy, socket)
        {
            _requestId = _proxy.NextRequestId;
            _proxy.NextRequestId = _proxy.NextRequestId + 1;
            _requestTimeout = LOCAL_REQUEST_PACKAGE_DEFAULT_TIMEOUT;
            _routines = routines;
            _defaultMethod = defaultMethod;
        }

        /// <summary>
        /// Handles an RC internal request.
        /// </summary>
        /// <returns>The status.</returns>
        public override Status HandleRequest()
        {
            LogDebug("Processing internal request: " + _originalRequest.RequestUri);

            String path = _originalRequest.RequestUri.LocalPath;
            RoutineMethod method = _routines.ContainsKey(path) ? _routines[path] : _defaultMethod;

            try
            {
                MethodInfo mInfo = this.GetType().GetMethod(method.MethodName);
                if (mInfo == null)
                {
                    LogDebug("Unknown method in internal handler: " + method.MethodName);
                    return RequestHandler.Status.Failed;
                }
                mInfo.Invoke(this, GetParameters(method));
            }
            catch (Exception)
            {
                return RequestHandler.Status.Failed;
            }
            return RequestHandler.Status.Completed;
        }

        private Object[] GetParameters(RoutineMethod method)
        {
            if (method.ParameterNames == null)
            {
                // No parameters
                return null;
            }
            Object[] result = new Object[method.ParameterNames.Length];
            // Parse parameters
            NameValueCollection parameterCollection = Util.ParseHtmlQuery(RequestUri);
            for (int i = 0; i < method.ParameterNames.Length; i++)
            {
                String parameterName = method.ParameterNames[i];
                Type parameterType = method.ParameterTypes[i];
                // Convert to required type
                result[i] = Convert.ChangeType(parameterCollection.Get(parameterName), parameterType);
            }
            return result;
        }
    }
}
