var surveyRequest;

// Asks the server and shows the survey, if the server says it is due
function showSurveyIfDue(endOfSession){
	// Ask server
	surveyRequest = new ajaxRequest();       
	surveyRequest.open("GET","request/isSurveyDue?endOfSession=" + endOfSession, true);
    surveyRequest.onreadystatechange = showSurvey;  
	surveyRequest.send(null);
}

// Shows the survey, if the server says it is due
function showSurvey() {
    if (surveyRequest.readyState==4 && surveyRequest.status==200){
		if (surveyRequest.response == "True"){
            window.showModalDialog('survey.html','','dialogHeight:400px;dialogWidth:300px;')
        }
    }
}