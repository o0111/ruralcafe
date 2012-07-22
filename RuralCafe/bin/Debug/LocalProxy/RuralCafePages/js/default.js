// JavaScript Document
window.onload = function() {
	if (document.getElementById("open_a")!=null){
		document.getElementById("open_a").onmouseover=changeBtn;
		document.getElementById("open_a").onmouseout=changeBtnBack;
	}
};

var _hidden=false;

function changeBtn(){
	if(!_hidden)
		document.getElementById("expand_img").src="img/minimize_btn_hover.png";
	else
		document.getElementById("expand_img").src="img/maximize_btn_hover.png";
}

function changeBtnBack(){
	if(!_hidden)
		document.getElementById("expand_img").src="img/minimize_btn.png";
	else
		document.getElementById("expand_img").src="img/maximize_btn.png";
}

function gotoPage(pagelink){
	if (window.frames && document.getElementById("main_frame")) {
		document.getElementById("main_frame").src = pagelink;//change this to index url
	}
	return false;
}

function tSearch(){
	var searchStr=document.getElementById('search_input').value;
	gotoPage('result.html?s='+searchStr);
	return false;
}
function openQueueDiv(){
	if(!_hidden){
		document.getElementById("expand_div").style.display="none";
		document.getElementById("expand_img").src="img/maximize_btn.png";
		document.getElementById("queue_div").style.height="20px";
		document.getElementById("main_div").style.bottom="15px";
	}
	else{
		document.getElementById("expand_div").style.display="block";
		document.getElementById("expand_img").src="img/minimize_btn.png";
		document.getElementById("queue_div").style.height="125px";
		document.getElementById("main_div").style.bottom="120px";
	}
	_hidden=!_hidden;
}