/* Satia Herfert 11-05-2013: sign up for Trotro*/


function signup() {
    var user = document.getElementById('username').value;
    var pass1 = document.getElementById('password1').value;
	var pass2 = document.getElementById('password2').value;
	
	if(user == "") {
		document.getElementById("wrong_username").innerHTML="enter a username";
		return false;
	}
	
	var xhttp= new ajaxRequest();
	if (xhttp.overrideMimeType)
		xhttp.overrideMimeType('text/xml');    
    xhttp.open("GET","users.xml", false);
    xhttp.send();
	
	// User check
	var xmlDoc = xhttp.responseXML;
	
	var xmlUsers = xmlDoc.getElementsByTagName('user');
	var userLen = xmlDoc.getElementsByTagName('customer').length;
	var xmlCustomers = xmlDoc.getElementsByTagName('customer');
	
	for (var i = 0; i < userLen; i++) {
		var xmlUser = xmlUsers[i].childNodes[0].nodeValue;
		
		if(xmlUser == user) {
			//user name exists already
			document.getElementById("wrong_username").innerHTML="Username exists already";
			return false;
		}
	}
    
	
	// password check
	if(pass1 == "") {
		document.getElementById("wrong_password").innerHTML="enter password in both fields";
		return false;
	} else if(pass1 != pass2) {
		document.getElementById("wrong_password").innerHTML="passwords don't match";
		return false;
	}
	
    // Create user (Local Proxy does that)
	sendSignupRequest(user, pass1, userLen + 1);
	return true;
}