using Apache.NMS;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Exceptions;
using RedShirt.Example.JobWorker.JobManagement.ActiveMq.Services;
using System.Text;

namespace RedShirt.Example.JobWorker.JobManagement.ActiveMq.UnitTests.Tests.Services;

public class ActiveMqMessageBodyRetrieverTests
{
    /// <summary>
    ///     Got an IBytesMessage
    /// </summary>
    [Fact]
    public void Test_Get_BytesMessage()
    {
        var bytesMessage = new Mock<IBytesMessage>();
        bytesMessage.Setup(m => m.BodyLength).Returns(12);
        bytesMessage.Setup(m => m.ReadBytes(It.IsAny<byte[]>())).Returns((byte[] output) =>
        {
            const string msg = "Hello World!";
            var prep = Encoding.UTF8.GetBytes(msg);
            prep.CopyTo(output);
            return msg.Length;
        });
        var retriever = new ActiveMqMessageBodyRetriever();
        var body = retriever.GetMessageBody(bytesMessage.Object);
        Assert.Equal("Hello World!", body);
    }

    /// <summary>
    ///     Test our fallback logic. If the item is something unexpected, then throw an exception so that it can be
    ///     investigated.
    /// </summary>
    [Fact]
    public void Test_Get_Exception()
    {
        var retriever = new ActiveMqMessageBodyRetriever();
        Assert.Throws<CouldNotRetrieveMessageBodyException>(() => retriever.GetMessageBody(null!));
    }

    /// <summary>
    ///     Test our fallback logic. If the item is something unexpected, then throw an exception so that it can be
    ///     investigated.
    /// </summary>
    [Fact]
    public void Test_Get_Exception_2()
    {
        var msg = new Mock<IMessage>();

        var retriever = new ActiveMqMessageBodyRetriever();
        Assert.Throws<CouldNotRetrieveMessageBodyException>(() => retriever.GetMessageBody(msg.Object));
    }

    /// <summary>
    ///     Got an ITextMessage
    /// </summary>
    [Fact]
    public void Test_Get_TextMessage()
    {
        var textMessage = new Mock<ITextMessage>();
        textMessage.Setup(m => m.Text).Returns("Hello World!");
        var retriever = new ActiveMqMessageBodyRetriever();
        var body = retriever.GetMessageBody(textMessage.Object);
        Assert.Equal("Hello World!", body);
    }
}