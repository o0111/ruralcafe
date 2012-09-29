/* Laura Li 09-06-2012: show the online search results*/

var xmlDocl = 0;	//xml file for search results
var xhttpl = 0;		//ajax request for retrieving search results
var noil=10; 		//number of items per page
var pageNuml=1; 	//the initial page number, if there are multiple pages 
var searchString="";//the search query string
var nop=10; 		//maximum number of links to results pages shown on each page

//send a ajax request to retrieve live search results
function loadLiveResult() {
	if (window.location.pathname) {
		var path = window.location.href;
		searchString = path.slice(path.search('s=')+2);
		//changfe here now no p is passed
		pageNuml = parseInt(path.slice(path.search('p=')+2,path.search('&'))) || 1;
		if (searchString != "")
			showResultl('request/search.xml?n='+noil+'&p='+pageNuml+'&s='+searchString);
	}
	else
		alert("Your browser does not support javascript");
}


addLoadEvent(loadLiveResult);

//show the live search results with given request url
function showResultl(requestURL) {
    xhttpl= new ajaxRequest();
	if (xhttpl.overrideMimeType)
		xhttpl.overrideMimeType('text/xml');
    xhttpl.onreadystatechange = showXMLl;        
    xhttpl.open("GET",requestURL,true);
    xhttpl.send(null);
}

//display the live search results in HTML
function showXMLl(){
	if (xhttpl.readyState==4){
		if (xhttpl.status==200){
			var xmldata=xhttpl.responseXML; //retrieve result as an XML object
			var total=xmldata.getElementsByTagName("search").item(0).attributes[0].nodeValue;
			if (document.getElementById('offline-search'))
				document.getElementById('offline-search').style.display="none";
			document.getElementById('live_count').innerHTML= 'Search returns '+total+' <span class="imp">live</span> results.';
			var innerHtml="";
			var results=xmldata.getElementsByTagName("item");
			for (var i=0;i<results.length;i++){
				//if url is not empty
				if(results[i].getElementsByTagName('url')[0].firstChild){
					var itemURL=results[i].getElementsByTagName('url')[0].firstChild.nodeValue;
					
					var itemTitle=results[i].getElementsByTagName('title')[0].firstChild?results[i].getElementsByTagName('title')[0].firstChild.nodeValue:itemURL;
					var itemSnippet=results[i].getElementsByTagName('snippet')[0].firstChild?results[i].getElementsByTagName('snippet')[0].firstChild.nodeValue:"";
					innerHtml+='<div class="result_page"><p><a href="http://'+itemURL+'" target="_parent">'+itemTitle+'</a></p><p class="url">'+itemURL+'</p><p>'+itemSnippet+'</p></div>';
				}
			}
			document.getElementById('live_updateArea').innerHTML=innerHtml;
			changelive_nav(total);
		}
		else{
			alert("An error has occured making the request");
		}
	}
}

//change the navigation links for a result page, given total number of pages and page number of the first page shown in the navigation links
function changelive_nav(total,startpage){
	if (total>noil){//results does not fit into one page
		var html="";
		var startNum=pageNuml-Math.floor(nop/2);
		var totalPage=Math.ceil(total/noil);
		if (startNum<1)
			startNum=1;
		for (var i=startNum;i<Math.min(nop+startNum,totalPage+1);i++){
			if (i!=pageNuml)
				html+=' <a href="result-online.html?p='+i+'&s='+searchString+'">'+i+'</a> ';
			else
				html+=' '+i+' ';
		}
		if (pageNuml!=1)
			html='<a href="result-online.html?p='+(pageNuml-1)+'&s='+searchString+'">Previous</a> &nbsp; &nbsp; &nbsp; &nbsp; '+html;
		if (pageNuml!=totalPage)
			html+='&nbsp; &nbsp; &nbsp; &nbsp;  <a href="result-online.html?p='+(pageNuml+1)+'&s='+searchString+'">Next</a>';
		document.getElementById('live_nav').innerHTML=html;
	}
}