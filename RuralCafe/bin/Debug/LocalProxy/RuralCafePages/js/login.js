var xmlDoc = 0;
var xhttp = 0;

function checkUser()
{
    var user = document.getElementById('username').value;
    var pass = document.getElementById('password').value;
	
    if(user == "" || pass == "")
    {
		
		document.getElementById("wrong_username").innerHTML=
		document.getElementById("wrong_password").innerHTML="";
        if(user == "")
        {
            document.getElementById("wrong_username").innerHTML="enter username";
        }

        if(pass == "")
        {
            document.getElementById("wrong_password").innerHTML="enter password";
        }
        return;
    }
    xhttp= new ajaxRequest();
	if (xhttp.overrideMimeType)
		xhttp.overrideMimeType('text/xml');
    xhttp.onreadystatechange = redirectUser;        
    xhttp.open("GET","users.xml",true);
    xhttp.send(null);
}

function redirectUser()
{
	if (xhttp!=0){
    var log = 1; // xml file is read
    var user = document.getElementById('username').value;
    var pass = document.getElementById('password').value;
    if (xhttp.readyState==4 && xhttp.status == 200)
    {
		document.getElementById("wrong_setting").innerHTML=
		document.getElementById("wrong_username").innerHTML=
		document.getElementById("wrong_password").innerHTML="";
        log = 2; // xml is ready
        xmlDoc = xhttp.responseXML;
		
        var xmlUsers = xmlDoc.getElementsByTagName('user');
        var xmlPasswords = xmlDoc.getElementsByTagName('pwd');
        var userLen = xmlDoc.getElementsByTagName('customer').length;
        var xmlCustomers = xmlDoc.getElementsByTagName('customer');
        for (var i = 0; i <  userLen; i++)
        {
            var xmlUser = xmlUsers[i].childNodes[0].nodeValue;
            var xmlPass = xmlPasswords[i].childNodes[0].nodeValue;
            var xmlId = xmlCustomers.item(i).attributes[0].nodeValue;
            if(xmlUser == user )
            {
				log = 3; //user name exist
				if (xmlPass == pass){
					log = 4;
					var path=window.location.href;
					alert(path);
					var index=path.search("t=");
					if (index!=-1)
						document.location="frame-offline-login.html?u="+xmlId+'&'+path.slice(index);
					else
						document.location="frame-offline-login.html?u="+xmlId;
					//document.cookie = xmlId;
					break;
				}
            }
        }

    }

    switch (log){
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