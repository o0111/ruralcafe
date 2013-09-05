/* Laura Li 09-06-2012: validate user name and password*/

// Check if there is something in the fields
function checkFields() {
    var user = document.getElementById('user').value;
    var pass = document.getElementById('pw').value;
	if (user == "") {
        document.getElementById("wrong_username").innerHTML="Enter a username";
		return false;
    }
	if (pass == "") {
		document.getElementById("wrong_password").innerHTML="Enter a password";
		return false;
	}
	
	// Set the location value
	document.getElementById('search').value = document.location.search;
	
	return true;
}