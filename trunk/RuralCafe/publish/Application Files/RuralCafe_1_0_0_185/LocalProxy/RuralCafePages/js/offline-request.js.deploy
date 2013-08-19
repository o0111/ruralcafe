var xmlDoc = 0;
var xhttp = 0;
var userid="";
var ipp = 7;//request items shown per page
var i;
var results;//loaded requests

function loadOffline() {
	document.getElementById('left_btn').onclick = scrollLeft;
	document.getElementById('right_btn').onclick = scrollRight;
	document.getElementById('close_box').onclick = closeFrame;
	if (window.location.pathname) {
		var path = window.location.href;
		if (path.indexOf('u=') != -1)
			if (path.indexOf('t=') != -1)
				userid=path.slice(path.indexOf('u=')+2,path.indexOf('t=')-1);
			else
				userid=path.slice(path.indexOf('u=')+2);
		else
			userid=get_cookie('uid');
		if (userid == "") {//redirect to the login page
			var index = path.indexOf("t=");
					if (index!=-1)
						document.location = "login.html?"+path.slice(index);
					else
						document.location = "login.html";
		}
		
		//checking pending request
		if (userid != "" && path.indexOf('t=') != -1 && (path.indexOf('a=')!= -1)){
			var requestTitle = path.slice(path.indexOf('t=')+2,path.indexOf('a=')-1);
			var requestURL = path.slice(path.indexOf('a=')+2);
			addRequest(requestTitle,requestURL);
		}
		else{
			loadQueue('request/queue.xml?u='+userid+'&v=0');
		}
	}
}

addLoadEvent(loadOffline);

function loadQueue(requestURL) {
    xhttp = new ajaxRequest();
	if (xhttp.overrideMimeType)
		xhttp.overrideMimeType('text/xml');
    xhttp.onreadystatechange = showXML;        
    xhttp.open("GET",requestURL,true);
    xhttp.send(null);
}

function addRequest(itemTitle,itemURL) {
	var mygetrequest = new ajaxRequest();
	mygetrequest.onreadystatechange = function() {
		if (mygetrequest.readyState == 4) {
			if (mygetrequest.status == 200) {
				var itemId = mygetrequest.responseText;
				//item id should be returned if it is successfully created
				//check whether item with that id is already added
				if (itemId && itemId!='' && !document.getElementById(itemId)) {		
					loadQueue('request/queue.xml?v=0');
					document.getElementById('main_frame').src = "newrequest.html";
				}
			}
			else {
				//alert("An error has occured adding the request");
			}
		}
	}
	//var tvalue=encodeURIComponent(itemTitle);
	//var avalue=encodeURIComponent(itemURL);
	mygetrequest.open("GET", "request/add?t="+itemTitle+'&a='+itemURL, true);
	mygetrequest.send(null);
	return false;
}

function removeRequest(itemId) {
	var mygetrequest = new ajaxRequest();
	mygetrequest.onreadystatechange = function() {
		if (mygetrequest.readyState == 4) {
			if (mygetrequest.status == 200) {
				loadQueue('request/queue.xml?v=0');
			}
			else {
				//alert("An error has occured removing the request");
			}
		}
	}
	var ivalue = encodeURIComponent(itemId);
	mygetrequest.open("GET", "request/remove?i="+ivalue, true);
	mygetrequest.send(null);
	return false;
}

function showXML(searchString) {
	if (xhttp.readyState == 4) {
		if (xhttp.status == 200) {
			var xmldata = xhttp.responseXML; //retrieve result as an XML object
			var innerHtml = '';
			results = xmldata.getElementsByTagName("item");
			for (i = 0; i < Math.min(ipp,results.length); i++) {
				innerHtml += itemHTML(i);
			}
			document.getElementById('update_area').innerHTML = innerHtml;
			document.getElementById('left_btn').style.display = "none";
			if (results.length <= ipp)
				document.getElementById('right_btn').style.display = "none";
			else
				document.getElementById('right_btn').innerHTML = (results.length-i);
		}
		else{
			//alert("An error has occured making the request");
		}
	}
}

function itemHTML(index) {
	var itemId = results.item(index).attributes[0].nodeValue;
	var itemTitle = results[index].getElementsByTagName('title')[0].firstChild.nodeValue;
	var itemURL = results[index].getElementsByTagName('url')[0].firstChild.nodeValue;
	var itemStatus = results[index].getElementsByTagName('status')[0].firstChild.nodeValue;
	var itemSize = results[index].getElementsByTagName('size')[0].firstChild.nodeValue;		
	return '<div id="'+itemId+'" class="queue_item"><span class="cancel_btn" onclick="removeRequest('+itemId+');"></span><span class="item_title">'+itemTitle+'</span><span class="status '+itemStatus+'">'+itemStatus+'</span><div class="queue_detail">'+itemTitle+'<br/><br/>'+itemURL+'</div></div>';
}

function scrollRight() {
	var parentNode = document.getElementById('update_area');
	parentNode.removeChild(parentNode.firstChild);
	parentNode.innerHTML+=itemHTML(i);
	i++;
	if (i-ipp>0)
		document.getElementById('left_btn').style.display="block";
	if (i>=results.length)
		document.getElementById('right_btn').style.display="none";
	document.getElementById('left_btn').innerHTML=(i-ipp);
	document.getElementById('right_btn').innerHTML=(results.length-i);
	return false;
}

function scrollLeft() {
	var parentNode  =document.getElementById('update_area');
	parentNode.removeChild(parentNode.lastChild);
	i--;
	parentNode.innerHTML = itemHTML(i)+parentNode.innerHTML;
	if (i - ipp <= 0)
		document.getElementById('left_btn').style.display = "none";
	if (i < results.length)
		document.getElementById('right_btn').style.display = "block";
	document.getElementById('left_btn').innerHTML = (i-ipp);
	document.getElementById('right_btn').innerHTML = (results.length-i);
	return false;
}
