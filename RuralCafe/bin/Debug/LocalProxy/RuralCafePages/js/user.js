
var xhttp = 0;
var userid="";
var ipp = 7;//request items shown per page
var i;
var results;//loaded requests

function loadOffline(){	
	document.getElementById('left_btn').onclick=scrollLeft;
	document.getElementById('right_btn').onclick=scrollRight;
	document.getElementById('close_box').onclick=closeFrame;
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

function loadQueue(requestURL){
	clearCountDown();
    xhttp= new ajaxRequest();
	if (xhttp.overrideMimeType)
		xhttp.overrideMimeType('text/xml');
    xhttp.onreadystatechange = showXML;        
    xhttp.open("GET",requestURL,true);
    xhttp.send(null);
}

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
	//var tvalue=encodeURIComponent(itemTitle);
	//var avalue=encodeURIComponent(itemURL);

	mygetrequest.open("GET", "request/add?u="+userid+"&t="+itemTitle+'&a='+itemURL, true);
	mygetrequest.send(null);
	return false;
}

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

function showXML(searchString){
	if (xhttp.readyState==4){
		if (xhttp.status==200){
			var xmldata=xhttp.responseXML; //retrieve result as an XML object
			var innerHtml='';
			results=xmldata.getElementsByTagName("item");
			//result.length=9 ipp=7 
			//queue = 8 7 6 5 4 3 2 
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

function itemHTML(index){
	var itemId=results.item(index).attributes[0].nodeValue;
	var itemTitle=results[index].getElementsByTagName('title')[0].firstChild.nodeValue;
	var itemURL=results[index].getElementsByTagName('url')[0].firstChild.nodeValue;
	var itemStatus=results[index].getElementsByTagName('status')[0].firstChild.nodeValue;
	var itemSize=results[index].getElementsByTagName('size')[0].firstChild.nodeValue;	
	var itemhtml="";
	if (itemStatus=="Completed")
		itemhtml= '<div id="'+itemId+'" class="queue_item complete_item"><div class="cancel_btn" onclick="removeRequest('+itemId+');"></div><span class="open_btn"  onclick="openPage(\''+itemURL+'\');"><span class="item_title">'+itemTitle+'</span><span class="status '+itemStatus+'" id="status_'+itemId+'">'+itemStatus+'</span></span>';
	else
		itemhtml= '<div id="'+itemId+'" class="queue_item"><div class="cancel_btn" onclick="removeRequest('+itemId+');"></div><span class="item_title">'+itemTitle+'</span><span class="status '+itemStatus+'" id="status_'+itemId+'">'+itemStatus+'</span>';
	itemhtml+='<div class="queue_detail">'+itemTitle+'<br/><br/><span id="url_'+itemId+'">'+itemURL+'</span>';
	if (itemStatus!="Pending")
		itemhtml+='<br/><br/>Size: '+itemSize;
	itemhtml+='</div></div>';
	if (itemStatus=="Downloading")
		startCountDown(itemId);
	return itemhtml;
}

function openPage(url){
	document.getElementById('main_frame').src=url;
}

var interval;
var itemIds;

function startCountDown(id){
	itemIds.push(id);
	if (!interval) {
        interval = window.setInterval('getEST()', 1000);
    }
}

//not sure whether i can use 1 clock to send 2 reuqeust simutaneosly, wait for testing
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
						//alert(index);
						if (est!=0){
							if (document.getElementById('status_'+itemIds[index]))
								document.getElementById('status_'+itemIds[index]).innerHTML=est.replace(/</g,'&lt;').replace(/>/g,'&gt;');
						}
						else{ //finish downloading
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
 


// button stop
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

function clearCountDown(){
	itemIds=new Array();
	window.clearInterval(interval);
	interval=false;
}

function isCountDown(itemId) {
	for (var j=0;j<itemIds.length;j++){
		if (itemIds[j]==itemId){
			return j;
		}
	}
	return -1;
}
		
function scrollRight(){
	//result.length=9 ipp=7 
			//queue= 8 7 6 5 4 3 2  
	//i=1
	var parentNode=document.getElementById('update_area');	
	stopCountDown(isCountDown(parentNode.firstChild.id));
	parentNode.removeChild(parentNode.firstChild);
	parentNode.innerHTML+=itemHTML(i);
	i--;
	//i=0
		//queue= 7 6 5 4 3 2 1
	if (results.length-ipp-i-1>0)
		document.getElementById('left_btn').style.display="block";
	// if (i+1 <1)
	if (i<0)
		document.getElementById('right_btn').style.display="none";
	document.getElementById('left_btn').innerHTML=(results.length-ipp-i-1);
	document.getElementById('right_btn').innerHTML=(i+1);
	return false;
}

function scrollLeft(){
	//result.length=9 ipp=7 
			//queue= 7 6 5 4 3 2 1
	//i=0
	var parentNode=document.getElementById('update_area');
	stopCountDown(isCountDown(parentNode.lastChild.id));
	parentNode.removeChild(parentNode.lastChild);
	i++;
	//i==1;
	//queue= 8 7 6 5 4 3 2
	parentNode.innerHTML=itemHTML(i+ipp)+parentNode.innerHTML;
	// if (results.length-ipp-i-1<1)
	if (results.length-ipp-i-1<1)
		document.getElementById('left_btn').style.display="none";
	// if (i+1>0)
	if (i+1>0)
		document.getElementById('right_btn').style.display="block";
	document.getElementById('left_btn').innerHTML=(results.length-ipp-i-1);
	document.getElementById('right_btn').innerHTML=(i+1);
	return false;
}

var animInterval=false;

function startAnimation(){
	var dIcon=document.getElementById("download_animation");
	dIcon.style.display='block';
	var d=185;
	dIcon.style.bottom=d+'px';
	animInterval=window.setInterval(function(){
		d-=12;
		dIcon.style.bottom=d+'px';
		if (d<65)
			stopAnimation();
	}, 50);
}

function stopAnimation(){
	window.clearInterval(animInterval);
	document.getElementById("download_animation").style.display='none';
}