var xmlDoc = 0;
var xhttp = 0;
var noi=10; //number of items per page
var pageNum=1; //the initial page number, if there are multiple pages 
var searchString="";
var nop=10; //maximum number of links to results pages shown on each page


function loadResult(){
	if (window.location.pathname){
		var path=window.location.href;
		searchString=path.slice(path.search('s=')+2);
		//changfe here now no p is passed
		if (searchString!="")
			showResult('request/result.xml?n='+noi+'&p='+pageNum+'&s='+searchString);
	}
	else
		alert("Your browser does not support javascript");
}

addLoadEvent(loadResult);

function showResult(requestURL){
    xhttp= new ajaxRequest();
	if (xhttp.overrideMimeType)
		xhttp.overrideMimeType('text/xml');
    xhttp.onreadystatechange = showXML;        
    xhttp.open("GET",requestURL,true);
    xhttp.send(null);
}

function showXML(){
	if (xhttp.readyState==4){
		if (xhttp.status==200){
			var xmldata=xhttp.responseXML; //retrieve result as an XML object
			var total=xmldata.getElementsByTagName("search").item(0).attributes[0].nodeValue;
			document.getElementById('count').innerHTML= 'Search returns '+total+' <span class="imp">local</span> results.';
			if (typeof xmlDocl == 'undefined') 
				document.getElementById('count').innerHTML+='<span id="offline-search">Search for <a href="#">"'+decodeURIComponent(searchString)+'"</a> later when I am online.</span>';
			var innerHtml="";
			var results=xmldata.getElementsByTagName("item");
			for (var i=0;i<results.length;i++){
				var itemTitle=results[i].getElementsByTagName('title')[0].firstChild.nodeValue;
				var itemURL=results[i].getElementsByTagName('url')[0].firstChild.nodeValue;
				var itemSnippet;
				if (results[i].getElementsByTagName('snippet')[0].firstChild)
					itemSnippet=results[i].getElementsByTagName('snippet')[0].firstChild.nodeValue;
				else itemSnippet='';
				innerHtml+='<div class="result_page"><p><a href="http://'+itemURL+'" target="_parent">'+itemTitle+'</a><img class="cached_icon" alt="cached" src="img/cached.png" /></p><p class="url">'+itemURL+'</p><p>'+itemSnippet+'</p></div>';
			}
			document.getElementById('updateArea').innerHTML=innerHtml;
			changeNav(total);
		}
		else{
			alert("An error has occured making the request");
		}
	}
}

function changeNav(total,startpage){
	if (total>noi){//results does not fit into one page
		var html="";
		var startNum=pageNum-Math.floor(nop/2);
		var totalPage=Math.ceil(total/noi);
		if (startNum<1)
			startNum=1;
		for (var i=startNum;i<Math.min(nop+startNum,totalPage+1);i++){
			if (i!=pageNum)
				html+=' <a href="result-offline.html?p='+i+'&s='+searchString+'">'+i+'</a> ';
			else
				html+=' '+i+' ';
		}
		if (pageNum!=1)
			html='<a href="result-offline.html?p='+(pageNum-1)+'&s='+searchString+'">Previous</a> &nbsp; &nbsp; &nbsp; &nbsp; '+html;
		if (pageNum!=totalPage)
			html+='&nbsp; &nbsp; &nbsp; &nbsp;  <a href="result-offline.html?p='+(pageNum+1)+'&s='+searchString+'">Next</a>';
		document.getElementById('nav').innerHTML=html;
	}
}