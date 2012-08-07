function trotroAddLoadEvent(func) {
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

function trotroAdhereLinks(){
	var links=document.getElementsByTagName('a');
	for (var i=0;i<links.length;i++){
		var oldlink=links.item(i).href;
		if (oldlink.indexOf('?')==-1)
			links.item(i).href=oldlink+'?trotro='+links.item(i).innerHTML;
		else
			links.item(i).href=oldlink+'&trotro='+links.item(i).innerHTML;
		alert(links.item(i).href);
	}
}

trotroAddLoadEvent(trotroAdhereLinks);