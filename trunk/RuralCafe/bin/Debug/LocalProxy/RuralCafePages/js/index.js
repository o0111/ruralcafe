
var xmlDoc = 0;
var xhttp = 0;
var category="root";
var noc=6; //number of categories
var noi=4; //number of items maximum per category
var nod=2; //total number of digits in noc and noi
var oldCategory="root";

window.onload=function (){
	showCategory("request/index.xml?c=6&n=4&s="+category);
}

function redirect(url){
	showCategory(url);
	return false;
}

function showCategory(requestURL){
    xhttp= new ajaxRequest();
	if (xhttp.overrideMimeType)
		xhttp.overrideMimeType('text/xml');
    xhttp.onreadystatechange = showXML;        
    xhttp.open("GET",requestURL,true);
    xhttp.send(null);
	changeNav(requestURL);
}

function showXML(){
	if (xhttp.readyState==4){
		if (xhttp.status==200 || window.location.href.indexOf("http")==-1){
			var xmldata=xhttp.responseXML; //retrieve result as an XML object
			var innerHtml="";
			var categories=xmldata.getElementsByTagName("category");
			for (var i=0;i<categories.length;i++){
				if (i%2==0) innerHtml+='<div class="index_row">';
				innerHtml+='<div class="index_cat">';
				var catTitle=categories.item(i).attributes[0].nodeValue;
				innerHtml+='<h2><a href="request/index.xml?c='+noc+'&n='+noi+'&s='+catTitle+'" onclick="return redirect(this.href);">'+catTitle+' &gt;&gt;</a></h2>';
				var catItem=categories[i].getElementsByTagName('item');
				for (var j=0;j<catItem.length;j++){
					var itemTitle=catItem[j].getElementsByTagName('title')[0].firstChild.nodeValue;
					var itemURL=catItem[j].getElementsByTagName('url')[0].firstChild.nodeValue;
					var itemSnippet=catItem[j].getElementsByTagName('snippet')[0].firstChild.nodeValue;
					innerHtml+='<div class="index_page"><p><a href="http://'
							+itemURL+'">'+itemTitle
							+'</a><img class="cached_icon" alt="cached" src="img/cached.png" /></p><p class="url">'
							+itemTitle+'</p><p>'+itemSnippet+'</p></div>';
				}
				innerHtml+="</div>";
				if (i%2==1) innerHtml+='</div>';
			}
			document.getElementById('updateArea').innerHTML=innerHtml;
		}
		else{
			alert("An error has occured making the request");
		}
	}
}

function changeNav(url){
	var newCategory=url.slice(url.search('&s=')+3);
	var navHistory=document.getElementById('nav').innerHTML;
	if (navHistory.search(newCategory)==-1){
		document.getElementById('nav').innerHTML=navHistory.slice(0,navHistory.search(oldCategory))+'<a href="request/index.xml?c='+noc+'&n='+noi+'&s='+oldCategory+'" onclick="return redirect(this.href);">'+oldCategory+'</a>&gt;&gt;'+newCategory;
		oldCategory=newCategory;
	}
	else {
		document.getElementById('nav').innerHTML=navHistory.slice(0,navHistory.search(newCategory)-43-nod)+newCategory;
		oldCategory=newCategory;
	}
}