var timeToShowTooltipMs = 250;
var suggestionAmount = 3;
var followingAndPrecedingWordCount = 3;

var suggestionRequest;	        //ajax request for retrieving suggestions
var rcOpentips = {};            // all tooltips in a dictionary.
var activeOpentip;              // if != null, this is the currently (or last) visible Opentip
var activeLinkNumber = -1;      // The number of the link where the mouse is over or -1

Opentip.styles.rcStyle = {
  tipJoint: "bottom",
  hideTrigger: "closeButton",
  hideOn: "closeButton",
  background: "#cccccc",
  borderColor: "#000000",
  closeButtonCrossColor: "#000000",
  shadow: false
};

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
            // already the active one. Might need to call show again,
            // if it has been closed
            activeOpentip.show();
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
            { target: "#rclink-"+linknumber, style: "rcStyle" });
        // Save it in the cache
        rcOpentips[linknumber] = activeOpentip;
        // Show temporary loading message XXX show() ?
        activeOpentip.setContent("Loading link suggestions...");
        // activeOpentip.show();
        
        // Extract url, anchorText and surrounding text.
        var linkNode = document.getElementById('rclink-'+linknumber);
        var url = linkNode.href;
        var anchorText = linkNode.textContent;
        
        var baseNode = linkNode;
        // If the linkCode is embedded in some markup (e.g. bold tags),
        // we might have to go up to its parent.
        // We do only do this once.
        if (!baseNode.nextSibling && !baseNode.previousSibling && baseNode.parentNode) {
            baseNode = baseNode.parentNode;
        }
        
        // Get the following and preciding words.
        var followingWords = [];
        var currentSibling = baseNode.nextSibling;
        while (currentSibling && followingWords.length < followingAndPrecedingWordCount) {
            var currentSiblingWords = currentSibling.textContent.split(/\s+/);
            for (var i = 0; i < currentSiblingWords.length
                 && followingWords.length < followingAndPrecedingWordCount; i++) {
                if (currentSiblingWords[i]) {
                    followingWords.push(currentSiblingWords[i]);
                }
            }
            currentSibling = currentSibling.nextSibling;
        }
        var precedingWords = [];
        currentSibling = baseNode.previousSibling;
        while (currentSibling && precedingWords.length < followingAndPrecedingWordCount) {
            var currentSiblingWords = currentSibling.textContent.split(/\s+/);
            for (var i = currentSiblingWords.length - 1; i >= 0
                 && precedingWords.length < followingAndPrecedingWordCount; i--) {
                if (currentSiblingWords[i]) {
                    precedingWords.push(currentSiblingWords[i]);
                }
            }
            currentSibling = currentSibling.previousSibling;
        }
        var surroundingText = precedingWords.reverse().join(" ") + " " + followingWords.join(" ");
        
        // build the request URL
        var rcRequestURL = "http://www.ruralcafe.net/request/linkSuggestions.xml?"
            + "url=" + encodeURIComponent(url)
            + "&anchor=" + encodeURIComponent(anchorText)
            + "&text=" + encodeURIComponent(surroundingText)
            + "&amount=" + encodeURIComponent(suggestionAmount);
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
    var suggestions = xmlData.firstChild;
    
    var rcHtml = '';
    if (suggestions.innerHTML == "cached") {
        // The target is cached, hence no suggestions.
        rcHtml = "That webpage was saved on: ";
        rcHtml += suggestions.getAttribute("downloadTime");
    } else {
        // Search box
        var searchBoxValue = suggestions.getAttribute("anchorText");
        rcHtml = '<div class="rclinksuggestionOuterBox">';
        rcHtml += '<form method="get" action="http://ruralcafe.net/result-offline.html">' +
            '<input class="rclinksuggestionSearchField" id="rcsearch_input' + linknumber + '"   type="text" name="s" value="' + searchBoxValue + '">' +
            '<input class="rclinksuggestionButton" type="submit" value="Local Search">' +
            '</form>';
        
        var status = suggestions.getAttribute("status");
        // Link suggestions
        rcHtml += '<div class="rclinksuggestionInnerBox">';
        rcHtml += "Your internet is " + status + ".";
        
        if (suggestions.children.length > 0) {
            rcHtml += " Try these similar websites:<br><br>";
            
            for (var i = 0; i < suggestions.children.length; i++) {
                var url = suggestions.children[i].getElementsByTagName('url')[0].firstChild.nodeValue;
                var title = suggestions.children[i].getElementsByTagName('title')[0].firstChild ?
                    suggestions.children[i].getElementsByTagName('title')[0].firstChild.nodeValue.trim() : url;
                // If trimmed title is empty
                if (!title) {
                    title = url;
                }
                // shrink long titles and urls
                if (title.length > 60) {
                    title = title.substring(0, 57) + "...";
                }
                var shortUrl = url;
                if (shortUrl.length > 60) {
                    shortUrl = shortUrl.substring(0, 57) + "...";
                }
                    
                var snippet = suggestions.children[i].getElementsByTagName('snippet')[0].firstChild ?
                    suggestions.children[i].getElementsByTagName('snippet')[0].firstChild.nodeValue : "";
                
                rcHtml += '<a class="rclinksuggestion" href="http://'+ url + '">' + title + '</a>';
                rcHtml += '<p class="rclinksuggestionURL">' + shortUrl + '</p>';
            }   
        }
        rcHtml += '</div></div>'
    }
    rcOpentips[linknumber].setContent(rcHtml);
}