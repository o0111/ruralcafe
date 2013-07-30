var suggestionRequest;	//ajax request for retrieving suggestions
var rcOpentips = {};    // all tooltips in a dictionary.
var activeOpentip;      // if != null, this is the currently (or last) visible Opentip

// TODO if the tooltip is above another link, you cannot click the links in the tooltip.

// shows a popup with 
function showSuggestions(linknumber) {
    // check if another one is still visible and hide it then
    if (activeOpentip) {
        if (activeOpentip == rcOpentips[linknumber]) {
            //already visible, just return
            return;
        }
        activeOpentip.hide();
        // abort the old request
        if (suggestionRequest) {
            // TODO check if there is a problem
            //suggestionRequest.abort();
        }
    }
    
    if (rcOpentips[linknumber]) {
        // If cached, use this one.
        activeOpentip = rcOpentips[linknumber];
        activeOpentip.show();
    }
    else {
        // Create new opentip
        activeOpentip = new Opentip("#rclink-"+linknumber,
            { target: true, tipJoint: "bottom", hideTrigger: "closeButton", hideOn: "mouseout" });
        // Save it in the cache
        rcOpentips[linknumber] = activeOpentip;
        // Show temporary loading message
        activeOpentip.setContent("Loading link suggestions...");
        
        // Extract url, anchorText and surrounding text.
        var linkNode = document.getElementById('rclink-'+linknumber);
        var url = linkNode.href;
        var anchorText = linkNode.textContent;
        // TODO include surrounding text
        var surroundingText = "";
        var rcRequestURL = "http://www.ruralcafe.net/request/linkSuggestions.xml?"
            + "url=" + url + "&anchor=" + anchorText + "&text=" + surroundingText;
        // Create ajax request to retrieve actual link suggestions
        var request = new ajaxRequest();
	if (request.overrideMimeType) {
		request.overrideMimeType('text/xml');
	}
        request.onreadystatechange = function()
            {
                if (request.readyState == 4) {
                    if (request.status == 200) {
                        //retrieve result as an XML object
                        showSuggestionsXML(request.responseXML, linknumber);
                    }
                    else {
                        rcOpentips[linknumber].setContent("Failed to load link suggestions.");
                    }
                    // Show the tip, if it is still the active one
                    if (activeOpentip==rcOpentips[linknumber]) {
                        activeOpentip.show();
                    }
                }
            }       
        request.open("GET",rcRequestURL,true);
        request.send(null);
    }
}

function showSuggestionsXML(xmlData, linknumber) {
    // TODO design nicer
    var rcHtml = "";
    var suggestions = xmlData.firstChild;
    
    if (suggestions.innerHTML == "cached") {
        // The target is cached, hence no suggestions.
        rcHtml = "The target is cached!";
    }
    for (var i = 0; i < suggestions.children.length; i++) {
        rcHtml += '<a href="'+ suggestions.children[i].innerHTML + '">Link ' + i + '</a><br>';
    }
    rcOpentips[linknumber].setContent(rcHtml);
}