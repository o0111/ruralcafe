var timeToShowTooltipMs = 250;

var suggestionRequest;	        //ajax request for retrieving suggestions
var rcOpentips = {};            // all tooltips in a dictionary.
var activeOpentip;              // if != null, this is the currently (or last) visible Opentip
var activeLinkNumber = -1;      // The number of the link where the mouse is over or -1

// Saves that the mouse currently isn't above a link
function clearActiveLinkNumber() {
    activeLinkNumber = -1;
}

// Schedules the actual function call after some time.
// Saves that the mouse is over that element.
function showSuggestions(linkNumber) {
    activeLinkNumber = linkNumber;
    setTimeout(function(){showSuggestions0(linkNumber)}, timeToShowTooltipMs);
}

// shows a popup with the link suggestions, if the mouse is still over the link
function showSuggestions0(linknumber) {
    // Abort if the mouse is not over that element any more.
    if (activeLinkNumber != linknumber) {
        return;
    }
    
    // check if another one is still visible and hide it then
    if (activeOpentip) {
        if (activeOpentip == rcOpentips[linknumber]) {
            //already visible, just return
            return;
        }
        activeOpentip.hide();
    }
    
    if (rcOpentips[linknumber]) {
        // If cached, use this one.
        activeOpentip = rcOpentips[linknumber];
        activeOpentip.show();
    }
    else {
        // Create new opentip, invisible until show() is called.
        activeOpentip = new Opentip("#rclink-trigger",
            { target: "#rclink-"+linknumber, tipJoint: "bottom",
            hideTrigger: "closeButton", hideOn: "closeButton"
            });
        // Save it in the cache
        rcOpentips[linknumber] = activeOpentip;
        // Show temporary loading message XXX show() ?
        activeOpentip.setContent("Loading link suggestions...");
        // activeOpentip.show();
        
        // Extract url, anchorText and surrounding text.
        var linkNode = document.getElementById('rclink-'+linknumber);
        var url = linkNode.href;
        var anchorText = linkNode.textContent;
        // TODO include surrounding text
        var surroundingText = "";
        var rcRequestURL = "http://www.ruralcafe.net/request/linkSuggestions.xml?"
            + "url=" + encodeURIComponent(url)
            + "&anchor=" + encodeURIComponent(anchorText)
            + "&text=" + encodeURIComponent(surroundingText);
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
    var rcHtml = "";
    var suggestions = xmlData.firstChild;
    
    if (suggestions.innerHTML == "cached") {
        // The target is cached, hence no suggestions.
        rcHtml = "The target is cached!";
    } else {
        // Search box
        var searchBoxValue = suggestions.getAttribute("anchorText");
        rcHtml = '<form method="get" action="http://ruralcafe.net/result-offline.html">' +
            '<input id="rcsearch_input' + linknumber + '"   type="text" name="s" value="' + searchBoxValue + '">' +
            '<input type="submit" value="Search Locally">' +
            '</form><hr class="rclinksuggestion" />';
        
        // Link suggestions
        rcHtml += "<b>Not available. Try these instead:</b><br><br>"
        for (var i = 0; i < suggestions.children.length; i++) {
            var url = suggestions.children[i].innerHTML;
            var title = suggestions.children[i].getAttribute("title");
            var downloadTime = suggestions.children[i].getAttribute("downloadTime");
            
            rcHtml += '<a class="rclinksuggestion" href="'+ url + '">' + title + '</a><br>';
            rcHtml += downloadTime + '<br><br>';
        }
    }
    
    rcOpentips[linknumber].setContent(rcHtml);
}