/* Laura Li 09-06-2012: show topics on the index page*/

var ixmlDoc = 0;	//xml object for the index topics
var ixhttp = 0;		//ajax requset for retrieving index topics
// TODO remove
var noc=6; 	//number of categories
var noi=4; //number of items maximum per category
var nod=2; //total number of digits in noc and noi

//initiate the index page
window.onload=function () {
	if (window.location.pathname) {
		var path = window.location.href;
		showCategory("clusters.xml");
	}
}

//send ajax request to retrieve topics
function showCategory(requestURL) {
    ixhttp= new ajaxRequest();
	if (ixhttp.overrideMimeType)
		ixhttp.overrideMimeType('text/xml');
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
			var clusters = xmldata.firstChild;
			var hierarchical = clusters.getAttribute("hierarchical"); // string!
			
			if(hierarchical == 'True') {
				// TODO
			} else {
				for (var i=0;i<clusters.children.length;i++) {
					innerHtml+='<div class="index_row"><div class="index_cat">';
					var clustLabels = clusters.children[i].getAttribute("features");
					innerHtml += '<h2>' + clustLabels + '</h2>';
					for (var j=0;j<clusters.children[i].children.length;j++) {
						var uri = clusters.children[i].children[j].innerHTML;
						innerHtml += '<div class="index_page"><p><a href="'+uri+'">'+uri+'</a></p></div>';
					}
					innerHtml += "</div></div>";
				}
			}
			
			
			document.getElementById('updateArea').innerHTML = innerHtml;
		}
		else {
			//alert("An error has occured making the request");
		}
	}
}

//rewrite the links for the topics so that search result pages for the topics are shown
function onSearch(catTitle,cat) {
	var status = get_cookie('status');
	if (status == "")
		alert("please enable cookie in your browser!");
	else
		cat.href = 'result-'+status+'.html?s='+catTitle;
}