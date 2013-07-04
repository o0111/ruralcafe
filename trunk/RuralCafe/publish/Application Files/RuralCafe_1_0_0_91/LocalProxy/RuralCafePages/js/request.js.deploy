/* Laura Li 09-06-2012: display message for new request*/

//check the network status and show the corresponding message in the message box
function loadMessage() {
	//check whether the referrer of the new request is known
	if (get_cookie('referrer') != "") {
		document.getElementById('back_link').innerHTML="Back to previous page";
		document.getElementById('back_link').href=get_cookie('referrer');
	}
	if (get_cookie('status') != "" && get_cookie('status') != "Offline")
		document.getElementById('message_div').innerHTML = '<h4>The page you are trying to view is added to the downloads list at the bottom of this page.</h4>';
}

window.onload = loadMessage;
