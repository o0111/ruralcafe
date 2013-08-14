/* Laura Li 09-06-2012: show the offline search results*/

var xmlDoc = 0;		//xml file for search results
var xhttp = 0;		//ajax request for retrieving search results
var noi=10; 		//number of items per page
var cachePageNum=1; 		//the initial page number, if there are multiple pages 
var livePageNum=1; 		//the initial live page number, if there are multiple pages 
var searchString="";//the search query string
var nop=10; 		//maximum number of links to results pages shown on each page

//send a ajax request to retrieve offline search results
function loadResult() {
	if (window.location.pathname) {
		var path = window.location.href;
		searchString=path.slice(path.search('s=')+2);
		//change here now no p is passed
		cachePageNum = parseInt(path.slice(path.search('cp=')+3,path.search('&'))) || 1;
                path = path.slice(path.search('&')+1); 
		livePageNum = parseInt(path.slice(path.search('lp=')+3,path.search('&'))) || 1;
		if (searchString != "")
			showResult('request/search-cache.xml?n='+noi+'&p='+cachePageNum+'&s='+searchString);
                else
                        document.getElementById('updateArea').innerHTML="<h2>You did not enter a search query!</h2>";

	}
	else
		alert("Your browser does not support javascript");
}

addLoadEvent(loadResult);

//show the offline search results with given request url
function showResult(requestURL) {
    xhttp= new ajaxRequest();
	if (xhttp.overrideMimeType)
		xhttp.overrideMimeType('text/xml');
    xhttp.onreadystatechange = showXML;        
    xhttp.open("GET",requestURL,true);
    xhttp.send(null);
}

//display the live search results in HTML
function showXML() {
	if (xhttp.readyState == 4) {
		if (xhttp.status == 200) {
			var xmldata = xhttp.responseXML; //retrieve result as an XML object
			var total = xmldata.getElementsByTagName("search").item(0).attributes[0].nodeValue;
			document.getElementById('count').innerHTML = 'Search returns '+total+' <span class="imp">local</span> results.';
			if (typeof xmlDocl == 'undefined') 
				document.getElementById('count').innerHTML += '<span id="offline-search">Search for <a href="#">"'+decodeURIComponent(searchString)+'"</a> later when I am online.</span>';
			var innerHtml = "";
			var results = xmldata.getElementsByTagName("item");
			for (var i = 0; i < results.length; i++) {
				//if url is not empty
				if(results[i].getElementsByTagName('url')[0].firstChild) {
					var itemURL = results[i].getElementsByTagName('url')[0].firstChild.nodeValue;
					var itemTitle = results[i].getElementsByTagName('title')[0].firstChild ? results[i].getElementsByTagName('title')[0].firstChild.nodeValue : itemURL;
					var itemSnippet = results[i].getElementsByTagName('snippet')[0].firstChild?results[i].getElementsByTagName('snippet')[0].firstChild.nodeValue:"";
					innerHtml += '<div class="result_page"><p><a href="http://'+itemURL+'" target="_newtab">'+itemTitle+'</a><img class="cached_icon" alt="cached" src="img/cached.png" /></p><p class="url">'+itemURL+'</p><p>'+itemSnippet+'</p></div>';
				}
			}
			document.getElementById('updateArea').innerHTML = innerHtml;
			changeNav(total);
		}
		else{
			alert("An error has occured making the request");
		}
	}
}

//change the navigation links for a result page, given total number of pages and page number of the first page shown in the navigation links
function changeNav(total,startpage) {
	if (total > noi) {//results does not fit into one page
		var html = "";
		var startNum = cachePageNum-Math.floor(nop/2);
		var totalPage = Math.ceil(total/noi);
                var displayTargetPage="";
                if(document.getElementById('live_updateArea'))
                        displayTargetPage="result-cached.html";
                else
                        displayTargetPage="result-offline.html";

		if (startNum < 1)
			startNum = 1;
		for (var i = startNum; i < Math.min(nop+startNum,totalPage+1); i++) {
			if (i != cachePageNum)
				html += ' <a href="'+displayTargetPage+'?cp='+i+'&lp='+livePageNum+'&s='+searchString+'">'+i+'</a> ';
			else
				html += ' '+i+' ';
		}
		if (cachePageNum != 1)
			html='<a href="'+displayTargetPage+'?cp='+(cachePageNum-1)+'&lp='+livePageNum+'&s='+searchString+'">Previous</a> &nbsp; &nbsp; &nbsp; &nbsp; '+html;
		if (cachePageNum != totalPage)
			html += '&nbsp; &nbsp; &nbsp; &nbsp;  <a href="'+displayTargetPage+'?cp='+(cachePageNum+1)+'&lp='+livePageNum+'&s='+searchString+'">Next</a>';
		document.getElementById('nav').innerHTML=html;
	}
}
