function logOut(){
	var date = new Date();
	date.setTime(date.getTime()-60);
	document.cookie="uid=;expires="+date.toGMTString()+"; path=/";
	document.cookie="uname=;expires="+date.toGMTString()+"; path=/";
	document.cookie="status=;expires="+date.toGMTString()+"; path=/";
}

window.onload=logOut;