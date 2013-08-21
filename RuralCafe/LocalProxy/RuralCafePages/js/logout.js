/* Laura Li 09-06-2012: sign out from Trotro*/

function logOut() {
	// Uncomment this if you don't want to show the user satisfaction survey
	showSurvey();
	
	// let the server know
	var xhttp= new ajaxRequest();       
	xhttp.open("GET","request/logout",true);
	xhttp.send(null);
	
	var date = new Date();
	date.setTime(date.getTime()-60);
	document.cookie = "uid=;expires="+date.toGMTString()+"; path=/";
	document.cookie = "uname=;expires="+date.toGMTString()+"; path=/";
	document.cookie = "status=;expires="+date.toGMTString()+"; path=/";
}

function showSurvey() {
	window.showModalDialog('survey.html','','dialogHeight:400px;dialogWidth:300px;')
}

window.onload = logOut;