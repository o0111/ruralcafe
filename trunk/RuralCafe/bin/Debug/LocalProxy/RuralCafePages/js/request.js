function loadReferer(){
	if (window.location.pathname){
		var path=window.location.href;
		refererurl=path.slice(path.search('r=')+2);
		//changfe here now no p is passed
		if (searchString!="")
			document.getElementById('back_link').href=refererurl;
	}
	else
		alert("Your browser does not support javascript");
}

addLoadEvent(loadReferer);
