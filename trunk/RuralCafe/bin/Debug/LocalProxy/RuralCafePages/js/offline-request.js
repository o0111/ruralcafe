var xmlDoc = 0;
var xhttp = 0;
var userid="";
var today=new Date();
var i=0;

//http://localhost/internetcafe/login.html?t=Research%20Assistant%20at%20STATMOS&u=http%3A%2F%2Fsweb.cityu.edu.hk%2Fdanli3%2Fportfolio%2Fresume.html%23statmos
//encodeURIComponent("http://sweb.cityu.edu.hk/danli3/portfolio/resume.html#statmos)")); in the unvisited links in webpage

window.onload=function (){
	if (window.location.pathname){
		var path=window.location.href;
		if (path.search('u=')!=-1)
			if (path.search('t=')!=-1)
				userid=path.slice(path.search('u=')+2,path.search('t=')-1);
			else
				userid=path.slice(path.search('u=')+2);
		else
			userid=get_cookie('uid');
		if (userid==""){//redirect to the login page
			var index=path.search("t=");
					if (index!=-1)
						document.location="login.html?"+path.slice(index);
					else
						document.location="login.html";
		}
		changeView(0,0);
		
		//checking pending request
		if (path.search('t=')!=-1 && (path.search('a=')!=-1)){
			var requestTitle=path.slice(path.search('t=')+2,path.search('a=')-1);
			var requestURL=path.slice(path.search('a=')+2);
			addRequest(requestTitle,requestURL);
		}
		
	}
}

function loadQueue(requestURL){
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
			if (mygetrequest.status==200 || window.location.href.indexOf("http")==-1){
				var itemId=mygetrequest.responseText;
				//item id should be returned if it is successfully created
				//check whether item with that id is already added
				if (itemId && itemId!='' && !document.getElementById(itemId)){
					var innerHtml=""
					itemTitle=decodeURIComponent(itemTitle);
					if (i%2==0)
						innerHtml+='<tr class="odd_tr" id="'+itemId+'">';
					else
						innerHtml+='<tr id="'+itemId+'">';
					innerHtml+='<td class="request_col">'+itemTitle+'</td><td class="finished status_col">Pending</td><td class="size_col">0</td><td class="action_col"><a href="'+itemURL+'" onclick="return removeRequest(\''+itemId+'\');">Remove</a></td></tr>';
					document.getElementById('queue_table').innerHTML+=innerHtml;
					i++;
				}
			}
			else{
				alert("An error has occured adding the request");
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
			if (mygetrequest.status==200 || window.location.href.indexOf("http")==-1){
				var itemTR = document.getElementById(itemId);
				itemTR.parentNode.removeChild(itemTR);
				i--;
			}
			else{
				alert("An error has occured removing the request");
			}
		}
	}
	var ivalue=encodeURIComponent(itemId);
	mygetrequest.open("GET", "request/remove?u="+userid+"&i="+ivalue, true);
	mygetrequest.send(null);
	return false;
}

function refreshRequest(itemId){
	var mygetrequest=new ajaxRequest()
	mygetrequest.onreadystatechange=function(){
		if (mygetrequest.readyState==4){
			if (mygetrequest.status==200 || window.location.href.indexOf("http")==-1){
				var itemTR = document.getElementById(itemId);
				//alert(itemTR);
				var itemTitle = itemTR.getElementsByTagName("td")[0].getElementsByTagName("a")[0].innerHTML;
				//alert(itemTitle);
				itemTR.parentNode.removeChild(itemTR);
				i--;
				var innerHtml="";
				if (i%2==0)
					innerHtml+='<tr class="odd_tr" id="'+itemId+'">';
				else
					innerHtml+='<tr id="'+itemId+'">';
				innerHtml+='<td class="request_col">'+itemTitle+'</td><td class="finished status_col">'+itemStatus+'</td><td class="size_col">0</td><td class="action_col"><a href="#" onclick="return removeRequest(\''+itemId+'\');">Remove</a></td></tr>';
				document.getElementById('queue_table').innerHTML+=innerHtml;
				i++;
			}
			else{
				alert("An error has occured refreshing the request");
			}
		}
	}
	var ivalue=encodeURIComponent(itemId);
	mygetrequest.open("GET", "request/refresh?u="+userid+"&i="+ivalue, true);
	mygetrequest.send(null);
	return false;
}

function showXML(searchString){
	if (xhttp.readyState==4){
		if (xhttp.status==200 || window.location.href.indexOf("http")==-1){
			var xmldata=xhttp.responseXML; //retrieve result as an XML object
			var innerHtml="";
			var results=xmldata.getElementsByTagName("item");
			for (i=0;i<results.length;i++){
				var itemId=results.item(i).attributes[0].nodeValue;
				var itemTitle=results[i].getElementsByTagName('title')[0].firstChild.nodeValue;
				var itemURL=results[i].getElementsByTagName('url')[0].firstChild.nodeValue;
				var itemStatus=results[i].getElementsByTagName('status')[0].firstChild.nodeValue;
				var itemSize=results[i].getElementsByTagName('size')[0].firstChild.nodeValue;
				if (i%2==0)
					innerHtml+='<tr class="odd_tr" id="'+itemId+'">';
				else
					innerHtml+='<tr id="'+itemId+'">';
				if (itemStatus=='Finished')
					innerHtml+='<td class="request_col"><a href="'+itemURL+'" onclick="return gotoPage(this.href);">'+itemTitle+'</a></td>';
				else
					innerHtml+='<td class="request_col">'+itemTitle+'</td>';
				innerHtml+='<td class="finished status_col">'+itemStatus+'</td><td class="size_col">'+itemSize+'</td><td class="action_col"><a href="'+itemURL+'" onclick="return removeRequest(\''+itemId+'\');">Remove</a>';
				if (itemStatus=='Finished')
					innerHtml+=' <a href="'+itemURL+'" onclick="return refreshRequest(\''+itemId+'\');">Refresh</a>';
				innerHtml+='</td></tr>';
			}
			document.getElementById('queue_table').innerHTML=innerHtml;
		}
		else{
			alert("An error has occured making the request");
		}
	}
}


function changeView(view, offset){
	var lastday=new Date();
	if (view==0){
		today.setDate(today.getDate()+offset);
		loadQueue('request/queue.xml?u='+userid+'&v='+("0" + today.getDate()).slice(-2)+'-'+("0" + (today.getMonth() + 1)).slice(-2)+'-'+today.getFullYear());
		document.getElementById('viewtag').innerHTML='View by: dates <a href="#" onclick="return changeView(1,0);">months</a> <a href="#" onclick="return changeView(2,0);">all</a>';
		var html='<a href="#" onclick="return changeView(0,-1);"><<</a> '+today.getDate()+'/'+(today.getMonth()+1)+'/'+today.getFullYear();
		if (lastday.getDate()==today.getDate())
			html+='&nbsp;&nbsp;&nbsp;';
		else
			html+=' <a href="#" onclick="return changeView(0,1);">>></a>';
		document.getElementById('timetag').innerHTML=html;
	}
	else if (view==1){
		today.setMonth(today.getMonth()+offset);
		loadQueue('request/queue.xml?u='+userid+'&v='+("0" + (today.getMonth() + 1)).slice(-2)+'-'+today.getFullYear());
		document.getElementById('viewtag').innerHTML='View by: <a href="#" onclick="return changeView(0,0);">dates</a> months <a href="#" onclick="return changeView(2,0);">all</a>';
		var months=['Jan','Feb','March','Apirl','May','June','July','Aug','Sep','Oct','Nov','Dec'];
		var html=months[today.getMonth()]+' '+today.getFullYear();
		html='<a href="#" onclick="return changeView(1,-1);"><<</a> '+html;
		if (lastday.getMonth()==today.getMonth() && lastday.getYear()==today.getYear()){
			html+='&nbsp;&nbsp;&nbsp;';
		}
		else{			
			html+=' <a href="#" onclick="return changeView(0,1);">>></a>';
		}
		document.getElementById('timetag').innerHTML=html;
	}
	else if (view==2){
		loadQueue('request/queue.xml?u='+userid+'&v=0');
		document.getElementById('viewtag').innerHTML='View by: <a href="#" onclick="return changeView(0,0);">dates</a> <a href="#" onclick="return changeView(0,0);">months</a> all';
		document.getElementById('timetag').innerHTML='&nbsp;';
	}
	return false;
}