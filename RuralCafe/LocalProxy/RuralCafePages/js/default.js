/* Laura Li 09-06-2012: funcitons for page redirect, searching, loading, and showing a greeting message */

//redirect the iframe to the a page link, used when a link is visited in the parent frame
function gotoPage(pagelink) {
	if (window.frames && document.getElementById("main_frame")) {
		document.getElementById("main_frame").src = pagelink; //change this to index url
	}
	return false;
}


//show search results, status can either be "cached", "online" or "online"
function tSearch(status) {
	var searchStr = document.getElementById('search_input').value;
	gotoPage('result-'+status+'.html?s='+searchStr);
	return false;
}

//redirect the window to show iframe only
function closeFrame(){
	document.location = document.getElementById('main_frame').src;
}

//call this function to add multiple adding load events
function addLoadEvent(func) {
	var oldonload = window.onload;
	if (typeof window.onload != 'function') {
		window.onload = func;
	}
	else {
		window.onload = function() {
		if (oldonload) {
			oldonload();
		}
		func();
		}
	}
}

//show a greeting message after signing in
function greetingMsg() {
	document.getElementById('internet_signin').innerHTML = "Hi, "+get_cookie('uname')+'<br /><a href="logout.html">Sign out</a>';
}
	