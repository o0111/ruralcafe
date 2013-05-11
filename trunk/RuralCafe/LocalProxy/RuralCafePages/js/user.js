/* Laura Li 09-06-2012: handle requests submitted by a user*/

var xhttp = 0;	//ajax request
var userid="";	//user id
var ipp = 7;	//request items shown per page
var i;			//index of first item in the queue
var results;	//loaded requests

//initiate the page, send a ajax request to fetch requests in user queue
function loadOffline(){	
	document.getElementById('left_btn').onclick=scrollLeft;
	document.getElementById('right_btn').onclick=scrollRight;
	if (window.location.pathname){
		var path=window.location.href;
		if (path.search('u=')!=-1)
			if (path.search('t=')!=-1)
				userid=path.slice(path.search('u=')+2,path.search('t=')-1);
			else
				userid=path.slice(path.search('u=')+2);
		else{
			userid=get_cookie('uid');
		}
		if (userid==""){//redirect to the login page
			var index=path.search("t=");
			if (index!=-1)
				document.location="login.html?"+path.slice(index);
			else
				document.location="login.html";
		}
		else{
			greetingMsg();
		}
		//checking pending request
		if (userid!="" && path.search('t=')!=-1 && (path.search('a=')!=-1)){
			var requestTitle=path.slice(path.search('t=')+2,path.search('a=')-1);
			var requestURL=path.slice(path.search('a=')+2);
			addRequest(requestTitle,requestURL);
		}
		else{
			loadQueue('request/queue.xml?u='+userid+'&v=0');
		}
	}
}

addLoadEvent(loadOffline);

//initiate the user queue
function loadQueue(requestURL){
	clearCountDown();
    xhttp= new ajaxRequest();
	if (xhttp.overrideMimeType)
		xhttp.overrideMimeType('text/xml');
    xhttp.onreadystatechange = showXML;        
    xhttp.open("GET",requestURL,true);
    xhttp.send(null);
}

//add a new request with given item title and item url 
function addRequest(itemTitle,itemURL){
	var mygetrequest=new ajaxRequest()
	mygetrequest.onreadystatechange=function(){
		if (mygetrequest.readyState==4){
			if (mygetrequest.status==200){
				var itemReferer=mygetrequest.responseText;
				//item id should be returned if it is successfully created
				//check whether item with that id is already added
				if (itemReferer && itemReferer!=''){		
					loadQueue('request/queue.xml?u='+userid+'&v=0');
					document.getElementById('main_frame').src="newrequest.html";
					startAnimation();
				}
			}
			else{
				//alert("An error has occured adding the request");
			}
		}
	}
	mygetrequest.open("GET", "request/add?u="+userid+"&t="+itemTitle+'&a='+itemURL, true);
	mygetrequest.send(null);
	return false;
}

//remove a request with given item id
function removeRequest(itemId){
	var mygetrequest=new ajaxRequest()
	mygetrequest.onreadystatechange=function(){
		if (mygetrequest.readyState==4){
			if (mygetrequest.status==200){
				loadQueue('request/queue.xml?u='+userid+'&v=0');
			}
			else{
				//alert("An error has occured removing the request");
			}
		}
	}
	var ivalue=encodeURIComponent(itemId);
	mygetrequest.open("GET", "request/remove?u="+userid+"&i="+ivalue, true);
	mygetrequest.send(null);
	return false;
}

//set the richness
function setRichness(richness){
	var mygetrequest=new ajaxRequest()
	mygetrequest.open("GET", "request/richness?r="+richness, true);
	mygetrequest.send(null);
	return false;
}

//signup
function sendSignupRequest(username, password, id){
	var mygetrequest=new ajaxRequest()
	mygetrequest.open("GET", "request/signup?u="+username+"&p="+password+"&i="+id, true);
	mygetrequest.send(null);
	return false;
}

//show the user queue in a bar
function showXML(searchString){
	if (xhttp.readyState==4){
		if (xhttp.status==200){
			var xmldata=xhttp.responseXML; //retrieve result as an XML object
			var innerHtml='';
			results=xmldata.getElementsByTagName("item");
			for (i=results.length-1;i>=Math.max(0,results.length-ipp);i--){
				innerHtml+=itemHTML(i);
			}
			//i=1
			document.getElementById('update_area').innerHTML=innerHtml;
			document.getElementById('left_btn').style.display="none";
			if (results.length<=ipp)
				document.getElementById('right_btn').style.display="none";
			else
				document.getElementById('right_btn').innerHTML=(i+1);
		}
		else{
			//alert("An error has occured making the request");
		}
	}
}

//perpare the html code for an item with given index
function itemHTML(index){
	var itemId=results.item(index).attributes[0].nodeValue;
	var itemTitle=results[index].getElementsByTagName('title')[0].firstChild.nodeValue;
	var itemURL=results[index].getElementsByTagName('url')[0].firstChild.nodeValue;
	var itemStatus=results[index].getElementsByTagName('status')[0].firstChild.nodeValue;
	var itemSize=results[index].getElementsByTagName('size')[0].firstChild.nodeValue;	
	var itemhtml="";
	if (itemStatus=="Completed")
		itemhtml= '<div id="'+itemId+'" class="complete_item"><div class="cancel_btn" onclick="removeRequest('+itemId+');"></div><span class="open_btn"  onclick="openPage(\''+itemURL+'\');"><span class="item_title">'+itemTitle+'</span><span class="status '+itemStatus+'" id="status_'+itemId+'">'+itemStatus+'</span></span><div class="queue_detail">'+itemTitle+'<br/><br/><span id="url_'+itemId+'"><a href='+itemURL+' target="_newtab">'+itemURL+'</a></span>';
	else
		itemhtml= '<div id="'+itemId+'" class="queue_item"><div class="cancel_btn" onclick="removeRequest('+itemId+');"></div><span class="item_title">'+itemTitle+'</span><span class="status '+itemStatus+'" id="status_'+itemId+'">'+itemStatus+'</span><div class="queue_detail">'+itemTitle+'<br/><br/><span id="url_'+itemId+'">'+itemURL+'</span>';
	if (itemStatus!="Pending")
		itemhtml+='<br/><br/>Size: '+itemSize;
	itemhtml+='</div></div>';
	//pending
	if (status!="offline")
		if (itemStatus=="Downloading" || itemStatus=="Pending")
			startCountDown(itemId);
	return itemhtml;
}

//open the url in a new tab
function openPage(url){
	window.open(url,'_newtab');
}

var interval;	//interval for checking request status and EST
var itemIds;	//an array of ids of the items in user queue

//start checking for request status and ETS every second
function startCountDown(id){
	itemIds.push(id);
	if (!interval) {
        interval = window.setInterval('getEST()', 1000);
    }
}

//retriev and display the request status and updating EST
function getEST(){
	for (var j=0;j<itemIds.length;j++){
		var index=j;
		var mygetrequests=new ajaxRequest();
		var updateEST=function(request, index){
			return function(){
			if (request.readyState==4){
				if (request.status==200){
					var est=request.responseText;
					if (est && est!=''){
						//pending.... est=-1
						if (est!='0' && est!='-1'){
							var statusspan=document.getElementById('status_'+itemIds[index]);
							if (statusspan){
								statusspan.innerHTML='<img src="img/downloading.gif" /> '+est.replace(/</g,'&lt;').replace(/>/g,'&gt;');
								statusspan.className="status Downloading";
							}
						}
						//else if downloading... est=0 
						else if (est=='0'){ //finish downloading
							stopCountDown(index);
							loadQueue('request/queue.xml?u='+userid+'&v=0');
						}
					}
				}
				else {
					stopCountDown(index);
				}
			}
			}
		}
		mygetrequests.onreadystatechange=updateEST(mygetrequests,index);
		mygetrequests.open("GET", "request/eta?u="+userid+"&i="+itemIds[index]);
		mygetrequests.send(null);
	}
}
 

//stop checking for the request status and ETA
function stopCountDown(itemIndex) {
	if (itemIndex!=-1){
		itemIds.splice(itemIndex,1);
		//alert(itemIds.toString());
		if (itemIds.length==0){
			//alert('stop clock');
			window.clearInterval(interval);
			intervalID = false;
		}
	}
}

//remove timer for checking the request status and ETA
function clearCountDown(){
	itemIds=new Array();
	window.clearInterval(interval);
	interval=false;
}

//check wether an item's status is being updated
function isCountDown(itemId) {
	for (var j=0;j<itemIds.length;j++){
		if (itemIds[j]==itemId){
			return j;
		}
	}
	return -1;
}

//sroll the bar to the right, show the next item
function scrollRight(){
	var parentNode=document.getElementById('update_area');	
	stopCountDown(isCountDown(parentNode.firstChild.id));
	parentNode.removeChild(parentNode.firstChild);
	parentNode.innerHTML+=itemHTML(i);
	i--;
	if (results.length-ipp-i-1>0)
		document.getElementById('left_btn').style.display="block";
	if (i<0)
		document.getElementById('right_btn').style.display="none";
	document.getElementById('left_btn').innerHTML=(results.length-ipp-i-1);
	document.getElementById('right_btn').innerHTML=(i+1);
	return false;
}

//sroll the bar to the left, show the previous item
function scrollLeft(){
	var parentNode=document.getElementById('update_area');
	stopCountDown(isCountDown(parentNode.lastChild.id));
	parentNode.removeChild(parentNode.lastChild);
	i++;
	parentNode.innerHTML=itemHTML(i+ipp)+parentNode.innerHTML;
	if (results.length-ipp-i-1<1)
		document.getElementById('left_btn').style.display="none";
	if (i+1>0)
		document.getElementById('right_btn').style.display="block";
	document.getElementById('left_btn').innerHTML=(results.length-ipp-i-1);
	document.getElementById('right_btn').innerHTML=(i+1);
	return false;
}

var animInterval=false;	//interval for showing an animated arrow

//start the animation for the arrow
function startAnimation(){
	var dIcon=document.getElementById("download_animation");
	dIcon.style.display='block';
	var d=185;
	dIcon.style.bottom=d+'px';
	animInterval=window.setInterval(function(){
		d-=10;
		dIcon.style.bottom=d+'px';
		if (d<68)
			stopAnimation();
	}, 50);
}

//stop animation for the arrow
function stopAnimation(){
	window.clearInterval(animInterval);
	//document.getElementById("download_animation").style.display='none';
}