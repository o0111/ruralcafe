// JavaScript Document

function gotoPage(pagelink){
	if (window.frames && document.getElementById("main_frame")) {
		document.getElementById("main_frame").src = pagelink;//change this to index url
	}
	return false;
}

function tSearch(status){
	var searchStr=document.getElementById('search_input').value;
	gotoPage('result-'+status+'.html?s='+searchStr);
	return false;
}

function closeFrame(){
	document.location=document.getElementById('main_frame').src;
}

function addLoadEvent(func) {
  var oldonload = window.onload;
  if (typeof window.onload != 'function') {
    window.onload = func;
  } else {
    window.onload = function() {
      if (oldonload) {
        oldonload();
      }
      func();
    }
  }
}

function greetingMsg(){
	document.getElementById('internet_signin').innerHTML="Hi, "+get_cookie('uname')+'<br /><a href="logout.html">Sign out</a>';
}
	