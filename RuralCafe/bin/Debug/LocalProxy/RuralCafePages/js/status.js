var status="offline";

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

function checkLogin(){
	if (get_cookie('uname')!="")
		greetingMsg();
}

addLoadEvent(checkStatus);
addLoadEvent(checkLogin);

function loadStatus(){
	var statusbar=document.getElementById('internet_status');
	statusbar.className.replace(' cached_status','');
	statusbar.className.replace(' online_status','');
	if (status!='offline')
		statusbar.className+=' '+status+'_status';
	statusbar.innerHTML=status;
	document.getElementById('tsearch').onsubmit=function(){
		return tSearch(status);
	};
}