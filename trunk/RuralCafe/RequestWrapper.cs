using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RuralCafe
{
    // wrapper class for a request object's state
    public class RequestWrapper
    {
        private GenericProxy _proxy;
        private GenericRequest _rootRequest;
        private RequestObject _currentRequestObject;
        private string _pageUri;
        private string _fileName;
        private int _objectStatus;

        public RequestWrapper(GenericProxy proxy, GenericRequest rootRequest, RequestObject currentRequestObject, string pageUri, string fileName, int status)
        {
            _proxy = proxy;
            _rootRequest = rootRequest;
            _currentRequestObject = currentRequestObject;
            _pageUri = pageUri;
            _fileName = fileName;
            _objectStatus = status;
        }

        public GenericProxy Proxy
        {
            get { return _proxy; }
        }
        public GenericRequest RootRequest
        {
            get { return _rootRequest; }
        }
        public RequestObject CurrentRequestObject
        {
            get { return _currentRequestObject; }
        }
        public string PageUri
        {
            get { return _pageUri; }
        }
        public string FileName
        {
            get { return _fileName; }
        }
        public int ObjectNum
        {
            get { return _objectStatus; }
            set { _objectStatus = value; }
        }
    }
}
