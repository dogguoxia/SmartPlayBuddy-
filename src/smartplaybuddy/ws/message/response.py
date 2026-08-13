from .message import Message


def response(action: str, data, To: str | None = None, RequestID: str | None = None):
    return Message(
        Type="response",
        Action=action,
        To=To,
        RequestID=RequestID,
        Data=data,
    ).to_json()
