using RuralCafe.Util;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Web;

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
            private String _methodName;
            private String[] _parameterNames;
            private Type[] _parameterTypes;

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
        /// A HTTP Error.
        /// </summary>
        [Serializable]
        protected class HttpException : Exception
        {
            /// <summary>
            ///  Construct an HttpError
            /// </summary>
            /// <param name="status">Error status.</param>
            public HttpException(HttpStatusCode status)
            {
                Status = status;
                Text = "";
            }

            /// <summary>
            ///  Construct an HttpError
            /// </summary>
            /// <param name="status">Error status.</param>
            /// <param name="strText">The reason for the status.</param>
            public HttpException(HttpStatusCode status, string strText)
            {
                Status = status;
                Text = strText;
            }

            /// <summary>
            /// The status code.
            /// </summary>
            public HttpStatusCode Status
            {
                get; private set;
            }
            /// <summary>
            /// The text.
            /// </summary>
            public String Text
            {
                get;
                private set;
            }
        }

        /// <summary>
        /// All routines must return a Response, if they do not throw a HttpException.
        /// </summary>
        public class Response
        {
            /// <summary>
            /// Empty response.
            /// </summary>
            public Response() { }

            /// <summary>
            /// Normal Response with a string.
            /// </summary>
            /// <param name="message">The message.</param>
            public Response(string message)
            {
                Message = message;
            }

            /// <summary>
            /// Only for the local internal request handler.
            /// </summary>
            /// <param name="addition">Either a message, or a filename to stream.</param>
            /// <param name="isStreamFileName">if true, addition is handled as a filename, as a message otherwise</param>
            public Response(string addition, bool isStreamFileName)
            {
                if (isStreamFileName)
                {
                    StreamFileName = addition;
                }
                else
                {
                    Message = addition;
                }
            }
            /// <summary>
            /// A message.
            /// </summary>
            public string Message
            {
                get; private set; 
            }
            /// <summary>
            /// A filename to stream its contents.
            /// </summary>
            public string StreamFileName
            {
                get; private set; 
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
        /// <param name="context">The client http context,</param>
        /// <param name="routines">The routines, mapped to the URL that should trigger them.</param>
        /// <param name="defaultMethod">The default method.</param>
        protected InternalRequestHandler(RCProxy proxy, HttpListenerContext context, 
            Dictionary<String, RoutineMethod> routines, RoutineMethod defaultMethod)
            : base(proxy, context, LOCAL_REQUEST_PACKAGE_DEFAULT_TIMEOUT)
        {
            _routines = routines;
            _defaultMethod = defaultMethod;
        }

        /// <summary>Dummy.</summary>
        public override void DispatchRequest(object nullObj)
        {
            // dummy
        }

        /// <summary>
        /// Handles an RC internal request.
        /// </summary>
        /// <returns>The status.</returns>
        public override void HandleRequest(object nullObj)
        {
            try
            {
                Logger.Debug("Processing internal request: " + _originalRequest.Url);

                String path = _originalRequest.Url.LocalPath;
                RoutineMethod method = _routines.ContainsKey(path) ? _routines[path] : _defaultMethod;

                MethodInfo mInfo = this.GetType().GetMethod(method.MethodName);
                if (mInfo == null)
                {
                    Logger.Error("Unknown method in internal handler: " + method.MethodName);
                    SendErrorPage(HttpStatusCode.InternalServerError, "Unknown method in internal handler: " + method.MethodName);
                    return;
                }
            
                Object result;
                try
                {
                    result = mInfo.Invoke(this, GetParameters(method));
                }
                catch (Exception e)
                {
                    // Get inner Exception
                    Exception innerE = e.InnerException;
                    if (innerE is HttpException)
                    {
                        // an intended Exception
                        HttpException httpe = (HttpException)innerE;
                        SendErrorPage(httpe.Status, httpe.Text);
                    }
                    else
                    {
                        // an unknown exception
                        string message = innerE != null ? innerE.Message : e.Message;
                        SendErrorPage(HttpStatusCode.InternalServerError, message);
                    }

                    return;
                }

                if (result == null || !(result is Response))
                {
                    Logger.Error("Return type wrong: " + method.MethodName);
                    SendErrorPage(HttpStatusCode.InternalServerError, "Return type wrong: " + method.MethodName);

                    return;
                }
                // Send result
                Response response = (Response)result;
                if (response.Message != null)
                {
                    // Specify content type as "text/html" if not set before
                    if (_clientHttpContext.Response.ContentType == null)
                    {
                        _clientHttpContext.Response.ContentType = "text/html";
                    }
                    SendMessage(response.Message);
                }
                else if (response.StreamFileName != null)
                {
                    try
                    {
                        StreamFromCacheToClient(response.StreamFileName, false);
                    }
                    catch (FileNotFoundException)
                    {
                        SendErrorPage(HttpStatusCode.NotFound, "page does not exist: " + _originalRequest.Url);
                    }
                    catch (Exception e)
                    {
                        SendErrorPage(HttpStatusCode.InternalServerError, e.Message);
                    }
                }
            }
            finally
            {
                DisconnectSocket();
            }
        }

        /// <summary>
        /// Parses the URI Parameters for the given method.
        /// </summary>
        /// <param name="method">The method to parse the parameters for.</param>
        /// <returns>The Parameters as an Object[].</returns>
        private Object[] GetParameters(RoutineMethod method)
        {
            if (method.ParameterNames == null)
            {
                // No parameters
                return null;
            }
            Object[] result = new Object[method.ParameterNames.Length];

            // Parse parameters
            NameValueCollection parameterCollection = HttpUtility.ParseQueryString(_originalRequest.Url.Query);
            for (int i = 0; i < method.ParameterNames.Length; i++)
            {
                String parameterName = method.ParameterNames[i];
                Type parameterType = method.ParameterTypes[i];
                // Convert to required type
                object value = parameterCollection.Get(parameterName);
                if(value == null && parameterType.IsPrimitive)
                {
                    if (parameterType == typeof(bool))
                    {
                        value = false;
                    }
                    else
                    {
                        value = 0;
                    }
                }
                result[i] = Convert.ChangeType(value, parameterType);
            }
            return result;
        }

        /// <summary>
        /// Creates only a response with a message, not for streaming files.
        /// </summary>
        /// <param name="originalResponse">The original HttpWebResponse.</param>
        /// <returns>The generated Response.</returns>
        protected Response createResponse(HttpWebResponse originalResponse)
        {
            _clientHttpContext.Response.ContentType = originalResponse.ContentType;
            // XXX: additional headers lost ATM
            string message = HttpUtils.StreamContent(originalResponse);
            return new Response(message);
        }
    }
}
