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
		var anchortext=links.item(i).innerHTML;
		var tagi=anchortext.indexOf('<');
		var tage=anchortext.indexOf('>');
		while (tagi!=-1 && tage!=-1){
			anchortext=anchortext.substring(0,tagi)+anchortext.substring(tage+1);
			tagi=anchortext.indexOf('<');
			tage=anchortext.indexOf('>');
		}
		anchortext=encodeURIComponent(anchortext);
		var inpagelink="";
		var tagl=oldlink.indexOf('#');
		if (tagl!=-1){
			inpagelink=oldlink.substring(tagl);
			oldlink=oldlink.substring(0,tagl);
		}	
		if (oldlink.indexOf('?')==-1)
			links.item(i).href=oldlink+'?trotro='+anchortext+inpagelink;
		else
			links.item(i).href=oldlink+'&trotro='+anchortext+inpagelink;

	}
}

trotroAddLoadEvent(trotroAdhereLinks);