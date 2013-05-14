﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net;
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
        protected class HttpException : Exception
        {
            HttpStatusCode _status;
            string _strReason;
            string _strText;

            /// <summary>
            ///  Construct an HttpError
            /// </summary>
            /// <param name="status">Error status.</param>
            /// <param name="strReason">The reason for the status.</param>
            public HttpException(HttpStatusCode status, string strReason)
            {
                _status = status;
                _strReason = strReason;
                _strText = "";
            }

            /// <summary>
            ///  Construct an HttpError
            /// </summary>
            /// <param name="status">Error status.</param>
            /// <param name="strReason">The reason for the status.</param>
            /// <param name="strText">Any additional text.</param>
            public HttpException(HttpStatusCode status, string strReason, string strText)
            {
                _status = status;
                _strReason = strReason;
                _strText = strText;
            }

            public HttpStatusCode Status
            {
                get { return _status; }
            }
            public String Reason
            {
                get { return _strReason; }
            }
            public String Text
            {
                get { return _strText; }
            }
        }

        /// <summary>
        /// All routines must return a Response, if they do not throw an HttpException.
        /// </summary>
        public class Response
        {
            private string _contentType;
            private string _additionalHeaders;
            private string _message;
            private string _streamFileName;

            public Response(string contentType)
            {
                _contentType = contentType;
                _message = "";
                _additionalHeaders = "";
            }

            public Response(string contentType, string message)
            {
                _contentType = contentType;
                _message = message;
                _additionalHeaders = "";
            }

            public Response(string contentType, string additionalHeaders, string message)
                : this(contentType, additionalHeaders, message, false)
            {
            }

            /// <summary>
            /// Only for the local internal request handler.
            /// </summary>
            /// <param name="contentType">The content type.</param>
            /// <param name="additionalHeaders">Additional headers.</param>
            /// <param name="addition">Either a message, or a filename to stream.</param>
            /// <param name="isStreamFileName">if true, addition is handled as a filename, as a message otherwise</param>
            public Response(string contentType, string additionalHeaders, string addition, bool isStreamFileName)
            {
                _contentType = contentType;
                _additionalHeaders = additionalHeaders;
                if (isStreamFileName)
                {
                    _streamFileName = addition;
                }
                else
                {
                    _message = addition;
                }
            }

            public string ContentType
            {
                get { return _contentType; }
            }
            public string Message
            {
                get { return _message; }
            }
            public string AdditionalHeader
            {
                get { return _additionalHeaders; }
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
        protected InternalRequestHandler(RCProxy proxy, Socket socket, 
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
                Object result = mInfo.Invoke(this, GetParameters(method));
                if (result == null || !(result is Response))
                {
                    LogDebug("Return type wrong: " + method.MethodName);
                    SendErrorPage(HttpStatusCode.InternalServerError, "", "Return type wrong: " + method.MethodName);
                    return RequestHandler.Status.Failed;
                }
                // Send result
                Response response = (Response)result;
                SendOkHeaders(response.ContentType, response.AdditionalHeader);
                if (response.Message != null)
                {
                    SendMessage(response.Message);
                }
                else if (response.StreamFileName != null)
                {
                    long bytesSent = StreamFromCacheToClient(response.StreamFileName);
                    if (bytesSent <= 0)
                    {
                        SendErrorPage(HttpStatusCode.NotFound, "page does not exist", RequestUri);
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
                    SendErrorPage(httpe.Status, httpe.Reason, httpe.Text);
                }
                else
                {
                    // an unknown exception
                    SendErrorPage(HttpStatusCode.InternalServerError, "", innerE.Message);
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

        /// <summary>
        /// Will hopefully be obsoleted, when we don't need "own" Responses any more...
        /// Creates only a response with a message, not for streaming files.
        /// </summary>
        /// <param name="originalResponse">The original HttpWebResponse.</param>
        /// <returns>The generated Response.</returns>
        public static Response createResponse(HttpWebResponse originalResponse)
        {
            string contentType = originalResponse.ContentType;
            // XXX: additional headers lost ATM
            // Will be fixed, when we only use HttpWebResponse
            string message = Util.StreamContent(originalResponse);
            return new Response(contentType, message);
        }
    }
}
