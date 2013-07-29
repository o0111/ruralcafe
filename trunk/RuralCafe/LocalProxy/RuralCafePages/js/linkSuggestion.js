var suggestionRequest;	//ajax request for retrieving suggestions
var rcOpentips = {};         // all tooltips in a dictionary.
var activeOpentip;      // if != null, this is the currently visible Opentip

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
            { target: true, tipJoint: "bottom", hideTrigger: "closeButton", hideOn: "mouseout",
            //ajax: true
            //ajax: "http://dict.leo.org/trainer/index.php?lp=ende&lang=de"
            //ajax: "http://www.ruralcafe.net/request/linkSuggestions"
            });
        // Save it in the cache
        rcOpentips[linknumber] = activeOpentip;
        // Show temporary laoding message
        activeOpentip.setContent("Loading link suggestions...");
        
        // TODO inlcude url, title, etc...
        var rcRequestURL = "http://www.ruralcafe.net/request/linkSuggestions.xml?url=http://bla.com";
        // Create ajax request to retrieve actual link suggestions
        suggestionRequest = new ajaxRequest();
	if (suggestionRequest.overrideMimeType) {
		suggestionRequest.overrideMimeType('text/xml');
	}
        suggestionRequest.onreadystatechange = showSuggestionsXML;        
        suggestionRequest.open("GET",rcRequestURL,true);
        suggestionRequest.send(null);
    }
}

function showSuggestionsXML() {
    if (suggestionRequest.readyState == 4) {
        if (suggestionRequest.status == 200) {
            //retrieve result as an XML object
            var rcXmldata = suggestionRequest.responseXML;
            
            // TODO design nicer
            var rcHtml = "";
            var suggestions = rcXmldata.firstChild;
            for (var i = 0; i < suggestions.children.length; i++) {
                rcHtml += '<a href="'+ suggestions.children[i].innerHTML + '">Link ' + i + '</a><br>';
            }
            activeOpentip.setContent(rcHtml);
        }
        else {
            activeOpentip.setContent("Failed to load link suggestions.");
        }
    }
}