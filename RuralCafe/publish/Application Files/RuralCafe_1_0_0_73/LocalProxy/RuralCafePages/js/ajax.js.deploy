/* Laura Li 09-06-2012: funcitons for creating ajax requests and getting cookies*/

//create a object to handle ajax request
function ajaxRequest() {
	var activexmodes = ["Msxml2.XMLHTTP", "Microsoft.XMLHTTP"]; //activeX versions to check for in IE
	if (window.ActiveXObject){ //Test for support for ActiveXObject in IE first (as XMLHttpRequest in IE7 is broken)
		for (var i=0; i<activexmodes.length; i++){
			try{
				return new ActiveXObject(activexmodes[i]);
			}
			catch(e){
				//suppress error
			}
		}
	}
	else if (window.XMLHttpRequest) // if Mozilla, Safari etc
		return new XMLHttpRequest();
	else
		return false;
}

//return the value of a cookie with given name, "" if cookie not exists
function get_cookie(name) {
	var searchstr = name + "=";
	var returnvalue = "";
	if (document.cookie.length > 0) {
		var offset = document.cookie.indexOf(searchstr);
    	// if cookie exists
    	if (offset != -1) {
      		offset += searchstr.length;
      		// set index of beginning of value
      		var end = document.cookie.indexOf(";", offset);
 			// set index of end of cookie value
      		if (end == -1) 
	  			end = document.cookie.length;
      		returnvalue=unescape(document.cookie.substring(offset, end));
      	}
   	}
  	return returnvalue;
}
