/* Laura Li 09-06-2012: sign out from Trotro*/

function logOut() {
	// Uncomment this if you don't want to show the user satisfaction survey
	showSurveyIfDue(true);
	
	// Send logout request to server
	var xhttp= new ajaxRequest();       
	xhttp.open("GET","request/logout",true);
	xhttp.send(null);
}

window.onload = logOut;