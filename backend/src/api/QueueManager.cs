using System.Threading.Channels;

namespace api;

public class QueueManager
{
    private readonly Channel<PaymentClientReq> _channel;

    public ChannelReader<PaymentClientReq> Reader => _channel.Reader;
    public ChannelWriter<PaymentClientReq> Writer => _channel.Writer;

    public QueueManager()
    {
        var options = new BoundedChannelOptions(15_000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = false,
            SingleWriter = false
        };
        
        _channel = Channel.CreateBounded<PaymentClientReq>(options);
    }
}