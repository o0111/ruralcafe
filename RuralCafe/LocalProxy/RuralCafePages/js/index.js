/* Laura Li 09-06-2012: show topics on the index page*/

var ixmlDoc = 0;	//xml object for the index topics
var ixhttp = 0;		//ajax request for retrieving index topics

//initiate the index page
window.onload=function () {
	showCategoryS("root");
}

function showCategoryS(s) {
	showCategory("/request/index.xml?s="+s);
	// Like this we won't follow links
	return false;
}

//send ajax request to retrieve topics
function showCategory(requestURL) {
    ixhttp= new ajaxRequest();
	if (ixhttp.overrideMimeType) {
		ixhttp.overrideMimeType('text/xml');
	}
    ixhttp.onreadystatechange = showXML;        
    ixhttp.open("GET",requestURL,true);
    ixhttp.send(null);
}

//display the index topics in html
function showXML() {
	if (ixhttp.readyState == 4) {
		if (ixhttp.status == 200) {
			var xmldata=ixhttp.responseXML; //retrieve result as an XML object
			var innerHtml = "";
			// FIXME In IE this gets the XML declaration instead of the first child!?
			var categories2 = xmldata.firstChild;
			var categories = xmldata.documentElement;
			var level = categories.getAttribute("level"); // string!
			
			if (level == '1') {
				innerHtml = showXMLLevel1(categories);
			} else if (level == '2') {
				innerHtml =showXMLLevel2(categories);
			} else if (level == '3') {
				innerHtml =showXMLLevel3(categories);
			} else {
				// something is wrong
			}
			document.getElementById('updateArea').innerHTML = innerHtml;
		}
		else {
			//alert("An error has occured making the request");
		}
	}
}

function showXMLLevel1(categories) {
	var innerHtml = "";
	for (var i=0;i<categories.childNodes.length;i++) {
		if (i%2==0) {
			innerHtml+='<div class="index_row">';
		}
		innerHtml += '<div class="index_cat">';
		
		var catTitle = categories.childNodes[i].getAttribute("title");
		var catId = categories.childNodes[i].getAttribute("id");
		innerHtml += '<h2><a href="" onclick="return showCategoryS(\''+catId+'\')">'
			+ catTitle + '</a></h2>';
		
		
		for (var j=0;j<categories.childNodes[i].childNodes.length;j++) {
			var subCatTitle = categories.childNodes[i].childNodes[j].getAttribute("title");
			var subCatId = categories.childNodes[i].childNodes[j].getAttribute("id");
			innerHtml += '<div class="index_subcat"><p><a href="" onclick="return showCategoryS(\''+catId+'.'+subCatId+'\')">'+subCatTitle+'</a></p></div>';
		}
		innerHtml += "</div>";
		if (i%2 == 1) {
			innerHtml += '</div>';
		}
	}
	return innerHtml;
}

function showXMLLevel2(categories) {
	var innerHtml = '';
	var catTitle = categories.childNodes[0].getAttribute("title");
	var catId = categories.childNodes[0].getAttribute("id")
	var subcategories = categories.childNodes[0].childNodes;
	
	innerHtml += '<h1>' + catTitle + '</h1><hr />';
	
	for (var i=0;i<subcategories.length;i++) {
		innerHtml += '<div class="index_cat_broad">';
		
		var subCatTitle = subcategories[i].getAttribute("title");
		var subCatId = subcategories[i].getAttribute("id");
		innerHtml += '<h3><a href="" onclick="return showCategoryS(\''+catId+'.'+subCatId+'\')">'+subCatTitle+'</a></h3>';
		
		for (var j=0;j<subcategories[i].childNodes.length;j++) {
			var item = subcategories[i].childNodes[j];
			var url = item.getElementsByTagName("url")[0].childNodes[0].nodeValue;
			if (url.slice(0, 7) != "http://") {
				url = "http://" + url;
			}
			var title = item.getElementsByTagName("title")[0].firstChild?item.getElementsByTagName("title")[0].firstChild.nodeValue:"";
			if( title == "") {
				title = url;
			}
			var snippet = item.getElementsByTagName('snippet')[0].firstChild?item.getElementsByTagName('snippet')[0].firstChild.nodeValue:"";
			innerHtml += '<div class="index_page"><p><a href="'+url+'" target="_blank">'+title+'</a><img class="cached_icon" alt="cached" src="img/cached.png" /></p><p class="url">'+url+
			'</p><p>'+snippet+'</p></div>';
		}
		innerHtml += "</div>";
	}
	return innerHtml;
}

function showXMLLevel3(categories) {
	var innerHtml = '';
	var cat = categories.childNodes[0];
	var catTitle = cat.getAttribute("title");
	var catId = cat.getAttribute("id");
	var subCat = cat.childNodes[0];
	var subCatTitle = subCat.getAttribute("title");
	var subCatId = subCat.getAttribute("id")
	
	innerHtml += '<h1><a href="" onclick="return showCategoryS(\''+catId+'\')">'+catTitle+'</a> / '
		+ subCatTitle + '</h1><hr />';
	innerHtml += '<div class="index_cat_broad">';
	for (var j=0;j<subCat.childNodes.length;j++) {
		var item = subCat.childNodes[j];
		var url = item.getElementsByTagName("url")[0].childNodes[0].nodeValue;
		if (url.slice(0, 7) != "http://") {
			url = "http://" + url;
		}
		var title = item.getElementsByTagName("title")[0].firstChild?item.getElementsByTagName("title")[0].firstChild.nodeValue:"";
		if( title == "") {
			title = url;
		}
		var snippet = item.getElementsByTagName('snippet')[0].firstChild?item.getElementsByTagName('snippet')[0].firstChild.nodeValue:"";
		innerHtml += '<div class="index_page"><p><a href="'+url+'" target="_blank">'+title+'</a><img class="cached_icon" alt="cached" src="img/cached.png" /></p><p class="url">'+url+
			'</p><p>'+snippet+'</p></div>';
	}
	innerHtml += "</div>";
	return innerHtml;
}