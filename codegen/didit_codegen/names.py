"""C# naming of resources, methods, types, properties and enum members (DESIGN.md sections 4 and 5.1)."""

from __future__ import annotations

import re

CSHARP_KEYWORDS = {
    "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const",
    "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern",
    "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface",
    "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override",
    "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
    "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof",
    "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
}

# Hand-written public types in namespace Didit. Generated types must not reuse these names.
RESERVED_TYPE_NAMES = {
    "DiditClient", "DiditRawClient", "DiditClientOptions", "RequestOptions", "DiditResponse", "BinaryResponse",
    "FileUpload", "Page", "Optional", "IStringEnum", "DiditException", "ConfigurationException",
    "ApiConnectionException", "ApiTimeoutException", "ApiStatusException", "BadRequestException",
    "AuthenticationException", "PermissionDeniedException", "NotFoundException", "ConflictException",
    "UnprocessableEntityException", "RateLimitException", "InternalServerException", "WebhookVerificationException",
    "WebhookVerificationReason", "DiditWebhooks", "DiditEvent", "SessionEvent", "SessionStatusUpdatedEvent",
    "SessionDataUpdatedEvent", "UserEvent", "UserStatusUpdatedEvent", "UserDataUpdatedEvent", "BusinessEvent",
    "BusinessStatusUpdatedEvent", "BusinessDataUpdatedEvent", "ActivityCreatedEvent", "TransactionEvent",
    "TransactionCreatedEvent", "TransactionStatusUpdatedEvent", "TravelRuleStatusUpdatedEvent",
    "WorkflowComplianceChangedEvent", "KybRegistrySearchResolvedEvent", "UnknownEvent", "WebhookEnvironment",
    "WebhookSignature", "Authentication", "AuthenticationOptions", "AccessTokenClaims", "IdTokenClaims",
    "AuthOrganizationClaim", "AuthTokenResponse", "AuthSession", "AuthStepUp", "PkcePair", "PublishableKey",
    "OpenIdConfiguration", "TokenVerificationException", "TokenVerificationReason", "Didit", "System", "Task",
    "JsonElement", "Uri", "Internal", "Resources",
}

_WORD_RE = re.compile(r"[A-Za-z0-9]+")


def words(text: str) -> list[str]:
    """Splits snake_case, kebab-case, spaced and camelCase text into words."""
    result: list[str] = []
    for chunk in _WORD_RE.findall(text):
        # camelCase and PascalCase boundaries; keeps acronyms together (IPRisk -> IP, Risk).
        result.extend(re.findall(r"[A-Z]+(?=[A-Z][a-z0-9])|[A-Z]?[a-z0-9]+|[A-Z]+|[0-9]+", chunk) or [chunk])
    return [w for w in result if w]


def pascal(text: str) -> str:
    """snake_case or free text to PascalCase. All-caps words become capitalised (URL -> Url)."""
    parts = []
    for word in words(text):
        if word.isupper() and len(word) > 1:
            word = word.lower()
        parts.append(word[0].upper() + word[1:])
    name = "".join(parts)
    if not name:
        return "Empty"
    if name[0].isdigit():
        name = "Value" + name
    return name


def camel(text: str) -> str:
    name = pascal(text)
    name = name[0].lower() + name[1:]
    return escape_identifier(name)


def escape_identifier(name: str) -> str:
    return "@" + name if name in CSHARP_KEYWORDS else name


def component_name(name: str) -> str:
    """A $ref component keeps its name, made a valid C# identifier."""
    cleaned = re.sub(r"[^A-Za-z0-9_]", "", name)
    if not cleaned:
        return "Component"
    if cleaned[0].isdigit():
        cleaned = "Schema" + cleaned
    return cleaned[0].upper() + cleaned[1:]


def resource_type_name(segments: list[str]) -> str:
    return "".join(pascal(s) for s in segments)


def method_name(method: str) -> str:
    return pascal(method) + "Async"


def enum_member_name(value: str) -> str:
    if value == "":
        return "Empty"
    return pascal(value)
