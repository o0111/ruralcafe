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

addLoadEvent(checkStatus);

function loadStatus(){
	var statusbar=document.getElementById('internet_status');
	if (statusbar.classList.contains('cached_status'))
		statusbar.classList.remove('cached_status');
	else if (statusbar.classList.contains('online_status'))
		statusbar.classList.remove('online_status');
	if (status!='offline')
		statusbar.classList.add(status+'_status');
	statusbar.innerHTML=status;
	document.getElementById('tsearch').onsubmit=function(){
		return tSearch(status);
	};
}