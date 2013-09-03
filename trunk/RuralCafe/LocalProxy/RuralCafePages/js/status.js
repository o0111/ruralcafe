/* Laura Li 09-06-2012: handle different network connectivity*/

var status="offline";	//the network status, can either be "offline", "online", or "cached"
var statusInterval=30000;

//check the network status every statusInterval milliseconds
function initiateStatusInterval(){
	checkStatus();
	window.setInterval('checkStatus()', statusInterval);
	loginGreeting();
}

//send a ajax request to check for the network status
function checkStatus(){
	var mygetrequest=new ajaxRequest()
	mygetrequest.onreadystatechange=function(){
		if (mygetrequest.readyState==4){
			if (mygetrequest.status==200){
				status=mygetrequest.responseText;
				document.cookie="status="+status;
				loadStatus();
			}
			else{
				//alert("An error has occured adding the request");
			}
		}
	}
	mygetrequest.open("GET", "request/status.xml");
	mygetrequest.send(null);
	return false;
}

//show greeting if user is logged in
function loginGreeting(){
	if (get_cookie('uname')!="")
		greetingMsg();
}

addLoadEvent(initiateStatusInterval);

//load the network status, register search function according to the network status 
function loadStatus(){
	if (status=="offline")
		document.getElementById('search_btn').value="offline";
	else if (status=="cached")
		document.getElementById('search_btn').value="offline & live";
	else if (status=="online")
		document.getElementById('search_btn').value="live";
	document.getElementById('tsearch').onsubmit=function(){
		return tSearch(status);
	};
}