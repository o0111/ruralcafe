/* Laura Li 09-06-2012: sign out from Trotro*/

function logOut() {
	// Uncomment this if you don't want to show the user satisfaction survey
	showSurvey();
	
	// Send logout request to server
	var xhttp= new ajaxRequest();       
	xhttp.open("GET","request/logout",true);
	xhttp.send(null);
	
}

function showSurvey() {
	window.showModalDialog('survey.html','','dialogHeight:400px;dialogWidth:300px;')
}

window.onload = logOut;