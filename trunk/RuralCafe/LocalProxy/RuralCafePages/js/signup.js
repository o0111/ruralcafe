/* Satia Herfert 11-05-2013: sign up for Trotro*/

// Check if there is something in the fields and if the PWs match
function checkFields() {
    var user = document.getElementById('user').value;
    var pass1 = document.getElementById('password1').value;
	var pass2 = document.getElementById('password2').value;
	
	if(user == "") {
		document.getElementById("wrong_username").innerHTML="Enter a username.";
		return false;
	}
	
	// password check
	if(pass1 == "") {
		document.getElementById("wrong_password").innerHTML="Enter password in both fields.";
		return false;
	} else if(pass1 != pass2) {
		document.getElementById("wrong_password").innerHTML="Passwords don't match.";
		return false;
	}
	
	return true;
}