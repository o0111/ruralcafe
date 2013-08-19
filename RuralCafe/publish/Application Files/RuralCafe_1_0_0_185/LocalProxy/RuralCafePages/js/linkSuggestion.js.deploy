var timeToShowTooltipMs = 250;
var suggestionAmount = 3;
var followingAndPrecedingWordCount = 5;
var stopwords = ['different', 'n', 'necessary', 'need', 'needed', 'needing', 'newest',
                'next', 'no', 'nobody', 'non', 'noone', 'not', 'nothing', 'now',
                'nowhere', 'of', 'off', 'often', 'new', 'old', 'older', 'oldest',
                'on', 'once', 'one', 'only', 'open', 'again', 'among', 'already',
                'about', 'above', 'against', 'alone', 'after', 'also', 'although',
                'along', 'always', 'an', 'across', 'b', 'and', 'another', 'ask',
                'c', 'asking', 'asks', 'backed', 'away', 'a', 'should', 'show',
                'came', 'all', 'almost', 'before', 'began', 'back', 'backing',
                'be', 'became', 'because', 'becomes', 'been', 'at', 'behind',
                'being', 'best', 'better', 'between', 'big', 'showed', 'ended',
                'ending', 'both', 'but', 'by', 'asked', 'backs', 'can', 'cannot',
                'number', 'numbers', 'o', 'few', 'find', 'finds', 'clearly', 
                'her', 'herself', 'come', 'could', 'd', 'did', 'here', 'beings',
                'fact', 'far', 'felt', 'become', 'first', 'for', 'four', 'from',
                'full', 'fully', 'furthers', 'gave', 'general', 'generally', 'get',
                'gets', 'gives', 'facts', 'go', 'going', 'good', 'goods', 'certain',
                'certainly', 'clear', 'great', 'greater', 'greatest', 'group', 'grouped',
                'grouping', 'groups', 'h', 'got', 'has', 'g', 'have', 'having',
                'he', 'further', 'furthered', 'had', 'furthering', 'itself', 'faces',
                'highest', 'him', 'himself', 'his', 'how', 'however', 'i', 'if',
                'important', 'interests', 'into', 'is', 'it', 'its', 'j', 'anyone',
                'anything', 'anywhere', 'are', 'area', 'areas', 'around', 'as', 'seconds',
                'see', 'seem', 'seemed', 'seeming', 'seems', 'sees', 'right', 'several',
                'shall', 'she', 'enough', 'even', 'evenly', 'over', 'p', 'part',
                'parted', 'parting', 'parts', 'per', 'down', 'place', 'places',
                'point', 'pointed', 'pointing', 'points', 'possible', 'present', 'presented',
                'presenting', 'ends', 'high', 'mrs', 'much', 'must', 'my', 'myself',
                'presents', 'down', 'problem', 'problems', 'put', 'puts', 'q', 'quite',
                'will', 'with', 'within', 'r', 'rather', 'really', 'room', 'rooms',
                's', 'said', 'same', 'right', 'showing', 'shows', 'side', 'sides',
                'since', 'small', 'smaller', 'smallest', 'so', 'some', 'somebody',
                'someone', 'something', 'somewhere', 'state', 'states', 'such', 'sure',
                't', 'take', 'taken', 'than', 'that', 'the', 'their', 'then',
                'there', 'therefore', 'these', 'x', 'thought', 'thoughts', 'three',
                'through', 'thus', 'to', 'today', 'together', 'too', 'took', 'toward',
                'turn', 'turned', 'turning', 'turns', 'two', 'still', 'u', 'under',
                'until', 'up', 'others', 'upon', 'us', 'use', 'used', 'uses',
                'v', 'very', 'w', 'want', 'wanted', 'wanting', 'wants', 'was',
                'way', 'we', 'well', 'wells', 'went', 'were', 'what', 'when',
                'where', 'whether', 'which', 'while', 'who', 'whole', 'y', 'year',
                'years', 'yet', 'you', 'everyone', 'everything', 'everywhere', 'young',
                'younger', 'youngest', 'your', 'yours', 'z', 'ever', 'works', 'every',
                'everybody', 'f', 'face', 'other', 'our', 'out', 'just', 'interesting',
                'high', 'might', 'k', 'keep', 'keeps', 'give', 'given', 'higher',
                'kind', 'knew', 'know', 'known', 'knows', 'l', 'large', 'largely',
                'last', 'later', 'latest', 'least', 'less', 'needs', 'never', 'newer',
                'let', 'lets', 'like', 'likely', 'long', 'high', 'longer', 'longest',
                'm', 'made', 'make', 'making', 'man', 'many', 'may', 'me', 'member',
                'members', 'men', 'more', 'in', 'interest', 'interested', 'most', 'mostly',
                'mr', 'opened', 'opening', 'new', 'opens', 'or', 'perhaps', 'order',
                'ordered', 'ordering', 'orders', 'differ', 'differently', 'do', 'does',
                'done', 'downed', 'downing', 'downs', 'they', 'thing', 'things', 'think',
                'thinks', 'this', 'those', 'ways', 'why', 'without', 'work', 'worked',
                'working', 'would', 'during', 'e', 'each', 'early', 'either', 'end',
                'though', 'still', 'whose', 'saw', 'say', 'says', 'them', 'second',
                'any', 'anybody'
              ];

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
                // XXX This will eleminate all non latin languages and also
                // chars with accents and the such.
                var word = currentSiblingWords[i].replace(/\W+/, "");
                if (word && stopwords.indexOf(word) == -1) {
                    followingWords.push(word);
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
                // XXX This will eleminate all non latin languages and also
                // chars with accents and the such.
                var word = currentSiblingWords[i].replace(/\W+/, "");
                if (word && stopwords.indexOf(word) == -1) {
                    precedingWords.push(word);
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