/* Laura Li 09-06-2012: show topics on the index page*/

var ixmlDoc = 0;	//xml object for the index topics
var ixhttp = 0;		//ajax requset for retrieving index topics
var noc=6; 	//number of categories
var noi=4; //number of items maximum per category

//initiate the index page
window.onload=function () {
	showCategorySCN("root", noc, noi);
	// TODO remove
	if (window.location.pathname) {
	}
}

function showCategorySCN(s, c, n) {
	showCategory("/request/index.xml?s="+s+"&c="+c+"&n="+n);
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
			var categories = xmldata.firstChild;
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
	for (var i=0;i<categories.children.length;i++) {
		if (i%2==0) {
			innerHtml+='<div class="index_row">';
		}
		innerHtml += '<div class="index_cat">';
		
		var catTitle = categories.children[i].getAttribute("title");
		var catId = categories.children[i].getAttribute("id");
		innerHtml += '<h2><a href="" onclick="return showCategorySCN(\''+catId+'\', '+noc+', '+noi+')">' + catTitle + '</a></h2>';
		
		for (var j=0;j<categories.children[i].children.length;j++) {
			var subCatTitle = categories.children[i].children[j].getAttribute("title");
			var subCatId = categories.children[i].children[j].getAttribute("id");
			innerHtml += '<div class="index_subcat"><p><a href="" onclick="return showCategorySCN(\''+catId+'.'+subCatId+'\', '+noc+', '+noi+')">'+subCatTitle+'</a></p></div>';
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
	var catTitle = categories.children[0].getAttribute("title");
	var catId = categories.children[0].getAttribute("id")
	var subcategories = categories.children[0].children;
	
	innerHtml += '<h1>' + catTitle + '</h1><hr />';
	
	for (var i=0;i<subcategories.length;i++) {
		innerHtml += '<div class="index_cat_broad">';
		
		var subCatTitle = subcategories[i].getAttribute("title");
		var subCatId = subcategories[i].getAttribute("id");
		innerHtml += '<h3><a href="" onclick="return showCategorySCN(\''+catId+'.'+subCatId+'\', '+noc+', '+noi+')">'+subCatTitle+'</a></h3>';
		
		for (var j=0;j<subcategories[i].children.length;j++) {
			var item = subcategories[i].children[j];
			var url = item.getElementsByTagName("url")[0].innerHTML;
			var title = item.getElementsByTagName("title")[0].innerHTML;
			if( title == "") {
				title = url;
			}
			innerHtml += '<div class="index_page"><p><a href="'+url+'" target="_blank">'+title+'</a></p></div>';
		}
		innerHtml += "</div>";
	}
	return innerHtml;
}

function showXMLLevel3(categories) {
	var innerHtml = '';
	var cat = categories.children[0];
	var catTitle = cat.getAttribute("title");
	var catId = cat.getAttribute("id");
	var subCat = cat.children[0];
	var subCatTitle = subCat.getAttribute("title");
	var subCatId = subCat.getAttribute("id")
	
	innerHtml += '<h1><a href="" onclick="return showCategorySCN(\''+catId+'\', '+noc+', '+noi+')">'+catTitle+'</a> / ' + subCatTitle + '</h1><hr />';
	innerHtml += '<div class="index_cat_broad">';
	for (var j=0;j<subCat.children.length;j++) {
			var item = subCat.children[j];
			var url = item.getElementsByTagName("url")[0].innerHTML;
			var title = item.getElementsByTagName("title")[0].innerHTML;
			if( title == "") {
				title = url;
			}
			innerHtml += '<div class="index_page"><p><a href="'+url+'" target="_blank">'+title+'</a></p></div>';
		}
	innerHtml += "</div>";
	return innerHtml;
}