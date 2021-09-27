# uTP - Micro Transport Protocol

Information from [BEP 29](http://www.bittorrent.org/beps/bep_0029.html), [bittorrent/libutp](https://github.com/bittorrent/libutp) at github and from reverse engineering based on Wireshark UDP dumps.

## Notes

 - uTP uses window based congestion control. Each socket has a max_window which determines the maximum number of bytes the socket may have in-flight at any given time. Any packet that has been sent, but not yet acked, is considered to be in-flight

## uTP Packet Header Format

All fields are in network byte order (big endian).

    0       4       8               16              24              32 (bits)
    +-------+-------+---------------+---------------+---------------+
    | type  | ver   | extension     | connection_id                 |
    +-------+-------+---------------+---------------+---------------+
    | timestamp_microseconds                                        |
    +---------------+---------------+---------------+---------------+
    | timestamp_difference_microseconds                             |
    +---------------+---------------+---------------+---------------+
    | wnd_size                                                      |
    +---------------+---------------+---------------+---------------+
    | seq_nr                        | ack_nr                        |
    +---------------+---------------+---------------+---------------+

### type
packet type, and is one of the following 4 bit ints

| Name     | id | Description |
| ---------|----|-------------|
| ST_DATA  | 0  | regular data packet, always has a payload
| ST_FIN   | 1  | Finalize the connection, is the last packet. seq_nr of this packet will be the last packet expected (though earlier packets may still be in flight)
| ST_STATE | 2  | State packet, used to transmit an acknowledgement of a packet, does not increase the seq_nr
| ST_RESET | 3  | Force closes the connection
| ST_SYN   | 4  | Connect/Sync packet, seq_nr is set to 1, connection_id is randomised, all subsequent packets sent on this connection are sent with the connection_id + 1

### version
protocol version, is always set to 1 and stored as a 4 bit int

### connection_id
This is a random, unique, 16 bit int, identifying all the packets that belong to the same connection. Initial packet is sent with this number, all subsequent packets are sent with connection_id + 1

### extension
not currently used in this library, always set to a 8 bit int of 0

### timestamp_microseconds
The timestamp in microseconds when the packet was sent, this should be as close to the actual transmit time as possible

### timestamp_difference_microseconds
Initialised as 0 for the ST_SYN packet, subsequent packets set this to the current timestamp in microseconds, minus the timestamp_microseconds of the last received packet.

### wnd_size
32 bit int specificied in bytes. This is the number of bytes currently sent, but not yet ACK-ed with a ST_STATE. When sending any packets to the remote peer, this must be set to the number of bytes left in the sockets receive buffer.

### seq_nr
16 bit int, initialised to 1 or any random positive number. incremented by 1 for each packet sent except for when receiving ST_STATE, serves as an order number for the receiving end.

### ack_nr
The seq_nr from the last packet received, unless it is the first ST_SYN, then it is seq_nr - 1.

## Congestion Control

Congestion Control uses ledbat congestion control and uses a calculated max_window and also uses wnd_size where max_window is initialised to INT_MAX.

    const CCONTROL_TARGET = 100 * 1000 // 100ms in microseconds
    const MAX_CWND_INCREASE_BYTES_PER_RTT = 3000
    const MIN_WINDOW_SIZE = 10
    const MAX_WINDOW_DECAY = 100 * 1000

    var current_msec = current_microseconds()

    var target = CCONTROL_TARGET
    if (target <= 0) target = CCONTROL_TARGET

    var min_rtt = min(min_rtt, current_msec - last_received_packet.timestamp)
    var our_delay = min(min(our_history), min_rtt)
    var off_target = target - our_delay
    var window_factor = min(bytes_acked, max_window) / max(max_window, bytes_acked)
    var delay_factor = off_target / target
    var scaled_gain = MAX_CWND_INCREASE_BYTES_PER_RTT * window_factor * delay_factor

    if (scaled_gain + max_window < MIN_WINDOW_SIZE) {
        max_window = MIN_WINDOW_SIZE
    } else {
        max_window = max_window + scaled_gain
    }

    max_window = max_window < MIN_WINDOW_SIZE ? MIN_WINDOW_SIZE : (max_window > socket_send_buff_size ? socket_send_buff_size : max_window) // set new max window
    // max_window is now the maximum number of bytes that can be sent with this packet

    //can we decay the max_window?
    if(current_msec - last_decay_msec >= MAX_WINDOW_DECAY) {
        max_window = max_window * 0.5
		last_decay_msec = current_msec

        if(max_window < MIN_WINDOW_SIZE) {
            max_window = MIN_WINDOW_SIZE
        }
    }

## Example Transaction

 - Local peer sends
   - type = ST_SYN
   - ver = 1
   - extension = 0
   - connection_id = 12345 //random number
   - timestamp_microseconds = 123456789000
   - timestamp_difference_microseconds = 0
   - wnd_size = 0
   - seq_nr = 1
   - ack_nr = 0
 - Remote peer receives and decodes the header, and responds with
   - type = ST_STATE
   - ver = 1
   - extension = 0
   - connection_id = 12347 // connection_id from the local peer + 1
   - timestamp_microseconds = 133456789000
   - timestamp_difference_microseconds = 125000 // difference between when the ST_SYN was sent, and when it was received
   - wnd_size = 1000000 // chosen window size
   - seq_nr = 1000 // remote peers own sequence number
   - ack_nr = 1 // seq_nr from the local peer
 - Local peer sends
   - type = ST_DATA
   - ver = 1
   - extension = 0
   - connection_id = 12347 // connection_id from the remote peer
   - timestamp_microseconds = 143456789000
   - timestamp_difference_microseconds = 125000 // difference between when the ST_STATE was sent, and when it was received
   - wnd_size = 1000000 // chosen window size
   - seq_nr = 2 // local peers own sequence number
   - ack_nr = 1000 // seq_nr from the remote peer
 - Remote peer receives and decodes the header, and responds with
   - type = ST_STATE
   - ver = 1
   - extension = 0
   - connection_id = 12347 // connection_id
   - timestamp_microseconds = 153456789000
   - timestamp_difference_microseconds = 125000 // difference between when the ST_DATA was sent, and when it was received
   - wnd_size = 1000000 // chosen window size
   - seq_nr = 1000 // remote peers own sequence number
   - ack_nr = 2 // seq_nr from the local peer
