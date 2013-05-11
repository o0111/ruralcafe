/* Laura Li 09-06-2012: validate user name and password*/

var xmlDoc = 0;	//xml object for user accounts
var xhttp = 0;	//ajax request for checking user account

//display error messages for empty fields, if user does not exist or wrong password
function redirectUser() {
	if (xhttp != 0) {
    var logcode = 1; // xml file is read
    var user = document.getElementById('username').value;
    var pass = document.getElementById('password').value;
    if (xhttp.readyState == 4 && xhttp.status == 200) {
		document.getElementById("wrong_setting").innerHTML = "";
		document.getElementById("wrong_username").innerHTML = "";
		document.getElementById("wrong_password").innerHTML = "";
        logcode = 2; // xml is ready
        xmlDoc = xhttp.responseXML;
		
        var xmlUsers = xmlDoc.getElementsByTagName('user');
        var xmlPasswords = xmlDoc.getElementsByTagName('pwd');
        var userLen = xmlDoc.getElementsByTagName('customer').length;
        var xmlCustomers = xmlDoc.getElementsByTagName('customer');
        for (var i = 0; i < userLen; i++) {
            var xmlUser = xmlUsers[i].childNodes[0].nodeValue;
            var xmlPass = xmlPasswords[i].childNodes[0].nodeValue;
            var xmlId = xmlCustomers.item(i).attributes[0].nodeValue;
            if(xmlUser == user) {
				logcode = 3; //user name exist
				if (xmlPass == pass){
					logcode = 4;
					var path = window.location.href;
					//alert(path);
					var index = path.search("t=");
					if (index != -1)
						document.location = "trotro-user.html?u="+xmlId+'&'+path.slice(index);
					else
						document.location = "trotro-user.html?u="+xmlId;
					document.cookie = "uid="+xmlId;
					document.cookie = "uname="+xmlUser;
					break;
				}
            }
		}
    }
    switch (logcode) {
		case 0:
			document.getElementById("wrong_setting").innerHTML="Sorry, this browser isn't equipped to read XML data";
			break;
		case 2:
			document.getElementById("wrong_username").innerHTML="username does not exist";
			break;
		case 3:
			document.getElementById("wrong_password").innerHTML="password is incorrect";
			break;
	}
	}
}

//send ajax request to load the xml file of user accounts
function checkUser() {
    var user = document.getElementById('username').value;
    var pass = document.getElementById('password').value;
    if (user == "" || pass == "") {
		document.getElementById("wrong_username").innerHTML="";
		document.getElementById("wrong_password").innerHTML="";
        if (user == "") {
            document.getElementById("wrong_username").innerHTML="enter username";
        }
		if (pass == "") {
            document.getElementById("wrong_password").innerHTML="enter password";
        }
        return false;
    }
    xhttp= new ajaxRequest();
	if (xhttp.overrideMimeType)
		xhttp.overrideMimeType('text/xml');
    xhttp.onreadystatechange = redirectUser;        
    xhttp.open("GET","users.xml",true);
    xhttp.send(null);
	return false;
}