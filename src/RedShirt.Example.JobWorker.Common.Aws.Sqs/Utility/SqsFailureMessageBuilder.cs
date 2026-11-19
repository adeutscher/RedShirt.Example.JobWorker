using System.Text;
using System.Text.Json;
using Amazon.SQS.Model;

namespace RedShirt.Example.JobWorker.Common.Aws.Sqs.Utility;

/// <summary>
///     Builds SQS messages for forwarding failed stream records (Kafka, Kinesis) to a failure queue.
///     <para>
///         Stream sources only advance once every record in a batch has been acknowledged, and a record is only
///         acknowledged after its failure has been forwarded. A record that SQS will never accept would therefore hold up
///         the stream for good, so this builder makes sure that every record has a form that SQS can accept.
///     </para>
///     <list type="bullet">
///         <item>A body that SQS accepts is sent unchanged, with no message attributes.</item>
///         <item>
///             A body containing characters that SQS rejects is sent as base64 of its UTF-8 bytes, with the
///             <see cref="BodyEncodingAttribute" /> attribute set to <see cref="Base64Encoding" />.
///         </item>
///         <item>
///             An empty body, or one that SQS still rejects (for example because it is too large), is replaced by a
///             small JSON reference, with the <see cref="BodyOmittedAttribute" /> attribute set to <c>true</c>. The
///             source message ID identifies the record so that it can be read again from the stream.
///         </item>
///     </list>
/// </summary>
public static class SqsFailureMessageBuilder
{
    public const string BodyEncodingAttribute = "BodyEncoding";
    public const string Base64Encoding = "base64";
    public const string BodyOmittedAttribute = "BodyOmitted";
    public const string SourceMessageIdAttribute = "SourceMessageId";

    /// <summary>
    ///     Build the first-choice failure message for a record: its body unchanged if SQS accepts it, otherwise a base64
    ///     form, otherwise (for an empty body) a reference.
    /// </summary>
    /// <param name="queueUrl">Failure queue URL.</param>
    /// <param name="sourceMessageId">ID of the record within its stream.</param>
    /// <param name="body">Record body.</param>
    /// <param name="sourceAttributes">
    ///     Extra identifying attributes (for example a shard ID) added to base64 and reference messages.
    /// </param>
    public static SendMessageRequest Build(string queueUrl, string sourceMessageId, string? body,
        IReadOnlyDictionary<string, string>? sourceAttributes = null)
    {
        if (string.IsNullOrEmpty(body))
        {
            return BuildReference(queueUrl, sourceMessageId, "Body is empty", sourceAttributes);
        }

        if (ContainsOnlyAllowedCharacters(body))
        {
            return new SendMessageRequest
            {
                QueueUrl = queueUrl,
                MessageBody = body
            };
        }

        var request = new SendMessageRequest
        {
            QueueUrl = queueUrl,
            MessageBody = Convert.ToBase64String(Encoding.UTF8.GetBytes(body)),
            MessageAttributes = BuildAttributes(sourceMessageId, sourceAttributes)
        };
        request.MessageAttributes[BodyEncodingAttribute] = StringAttribute(Base64Encoding);
        return request;
    }

    /// <summary>
    ///     Build a reference message that identifies a record without carrying its body.
    /// </summary>
    /// <param name="queueUrl">Failure queue URL.</param>
    /// <param name="sourceMessageId">ID of the record within its stream.</param>
    /// <param name="reason">Short reason that the body was left out. Must not contain record content.</param>
    /// <param name="sourceAttributes">Extra identifying attributes (for example a shard ID).</param>
    public static SendMessageRequest BuildReference(string queueUrl, string sourceMessageId, string reason,
        IReadOnlyDictionary<string, string>? sourceAttributes = null)
    {
        var request = new SendMessageRequest
        {
            QueueUrl = queueUrl,
            MessageBody = JsonSerializer.Serialize(new ReferenceBody
            {
                SourceMessageId = sourceMessageId,
                SourceAttributes = sourceAttributes,
                Reason = reason
            }),
            MessageAttributes = BuildAttributes(sourceMessageId, sourceAttributes)
        };
        request.MessageAttributes[BodyOmittedAttribute] = StringAttribute("true");
        return request;
    }

    /// <summary>
    ///     Whether a request is a reference message built by <see cref="BuildReference" />.
    /// </summary>
    public static bool IsReference(SendMessageRequest request)
    {
        return request.MessageAttributes?.ContainsKey(BodyOmittedAttribute) == true;
    }

    /// <summary>
    ///     Whether every character is in the set that SQS accepts in a message body:
    ///     #x9, #xA, #xD, #x20 to #xD7FF, #xE000 to #xFFFD, and #x10000 to #x10FFFF.
    /// </summary>
    public static bool ContainsOnlyAllowedCharacters(string body)
    {
        for (var i = 0; i < body.Length; i++)
        {
            var c = body[i];
            if (char.IsHighSurrogate(c))
            {
                // A well-formed surrogate pair encodes #x10000 to #x10FFFF, which is allowed.
                if (i + 1 < body.Length && char.IsLowSurrogate(body[i + 1]))
                {
                    i++;
                    continue;
                }

                return false;
            }

            var allowed = c is '\t' or '\n' or '\r'
                          || c is >= ' ' and <= '퟿'
                          || c is >= '' and <= '�';
            if (!allowed)
            {
                return false;
            }
        }

        return true;
    }

    private static Dictionary<string, MessageAttributeValue> BuildAttributes(string sourceMessageId,
        IReadOnlyDictionary<string, string>? sourceAttributes)
    {
        var attributes = new Dictionary<string, MessageAttributeValue>
        {
            [SourceMessageIdAttribute] = StringAttribute(sourceMessageId)
        };

        if (sourceAttributes is null)
        {
            return attributes;
        }

        foreach (var (key, value) in sourceAttributes)
        {
            attributes[key] = StringAttribute(value);
        }

        return attributes;
    }

    private static MessageAttributeValue StringAttribute(string value)
    {
        return new MessageAttributeValue
        {
            DataType = "String",
            StringValue = value
        };
    }

    private sealed class ReferenceBody
    {
        public bool BodyOmitted => true;
        public required string SourceMessageId { get; init; }
        public IReadOnlyDictionary<string, string>? SourceAttributes { get; init; }
        public required string Reason { get; init; }
    }
}
