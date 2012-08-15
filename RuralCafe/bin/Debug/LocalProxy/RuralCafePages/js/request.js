function loadReferer(){
	if(get_cookie('referrer')!=""){
		document.getElementById('back_link').innerHTML="Back to previous page";
		document.getElementById('back_link').href=get_cookie('referrer');
	}
	if (get_cookie('status')!="" && get_cookie('status')!="Offline")
		document.getElementById('message_div').innerHTML='<h4>The page you are trying to view is being downloaded now. </h4><h4>Please continue browsing when Trotro is getting the page.</h4>';
}

window.onload=loadReferer;
