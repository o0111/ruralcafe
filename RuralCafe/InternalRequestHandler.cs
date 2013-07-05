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
            HttpStatusCode _status;
            string _strText;

            /// <summary>
            ///  Construct an HttpError
            /// </summary>
            /// <param name="status">Error status.</param>
            public HttpException(HttpStatusCode status)
            {
                _status = status;
                _strText = "";
            }

            /// <summary>
            ///  Construct an HttpError
            /// </summary>
            /// <param name="status">Error status.</param>
            /// <param name="strReason">The reason for the status.</param>
            public HttpException(HttpStatusCode status, string strText)
            {
                _status = status;
                _strText = strText;
            }

            public HttpStatusCode Status
            {
                get { return _status; }
            }
            public String Text
            {
                get { return _strText; }
            }
        }

        /// <summary>
        /// All routines must return a Response, if they do not throw a HttpException.
        /// </summary>
        public class Response
        {
            private string _message;
            private string _streamFileName;

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
                _message = message;
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
                    _streamFileName = addition;
                }
                else
                {
                    _message = addition;
                }
            }

            public string Message
            {
                get { return _message; }
            }
            public string StreamFileName
            {
                get { return _streamFileName; }
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
        protected InternalRequestHandler(RCProxy proxy, HttpListenerContext context, 
            Dictionary<String, RoutineMethod> routines, RoutineMethod defaultMethod)
            : base(proxy, context)
        {
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
            Logger.Debug("Processing internal request: " + _originalRequest.Url);

            String path = _originalRequest.Url.LocalPath;
            RoutineMethod method = _routines.ContainsKey(path) ? _routines[path] : _defaultMethod;

            try
            {
                MethodInfo mInfo = this.GetType().GetMethod(method.MethodName);
                if (mInfo == null)
                {
                    Logger.Error("Unknown method in internal handler: " + method.MethodName);
                    return RequestHandler.Status.Failed;
                }
                Object result = mInfo.Invoke(this, GetParameters(method));
                if (result == null || !(result is Response))
                {
                    Logger.Error("Return type wrong: " + method.MethodName);
                    SendErrorPage(HttpStatusCode.InternalServerError, "Return type wrong: " + method.MethodName);
                    return RequestHandler.Status.Failed;
                }
                // Specify content type as "text/html" if not set before
                if (_clientHttpContext.Response.ContentType == null)
                {
                    _clientHttpContext.Response.ContentType = "text/html";
                }
                // Send result
                Response response = (Response)result;
                if (response.Message != null)
                {
                    SendMessage(response.Message);
                }
                else if (response.StreamFileName != null)
                {
                    long bytesSent = StreamFromCacheToClient(response.StreamFileName);
                    if (bytesSent <= 0)
                    {
                        SendErrorPage(HttpStatusCode.NotFound, "page does not exist: " + RequestUri);
                        return RequestHandler.Status.Failed;
                    }
                }
                return RequestHandler.Status.Completed;
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
                return RequestHandler.Status.Failed;
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
                //_originalRequest.
                // Convert to required type
                result[i] = Convert.ChangeType(parameterCollection.Get(parameterName), parameterType);
            }
            return result;
        }

        /// <summary>
        /// Creates only a response with a message, not for streaming files.
        /// </summary>
        /// <param name="originalResponse">The original HttpWebResponse.</param>
        /// <returns>The generated Response.</returns>
        public static Response createResponse(HttpWebResponse originalResponse)
        {
            string contentType = originalResponse.ContentType;
            // XXX: additional headers lost ATM
            string message = HttpUtils.StreamContent(originalResponse);
            return new Response(message);
        }
    }
}
