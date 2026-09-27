/** Notified for every gRPC-Web message frame arriving on the Attach stream. */
export interface FrameListener {
  /** A new message starts; its full size is already known from the header. */
  onFrameStart(totalBytes: number): void;
  /** More bytes of the current message have arrived. */
  onFrameProgress(receivedBytes: number, totalBytes: number): void;
}

/**
 * A fetch() that behaves exactly like the normal one, except that the body of the
 * Attach response is passed through a byte counter. Nothing is changed or delayed.
 */
export function createWatchedFetch(listener: FrameListener): typeof fetch {
  return async (input, init) => {
    const response = await fetch(input, init);

    const url = typeof input === "string" ? input : input instanceof URL ? input.href : input.url;
    if (!url.endsWith("/grpcviewport.v1.ViewerLink/Attach") || !response.body) {
      return response; // every other call (e.g. ReportCamera later) untouched
    }

    const watched = response.body.pipeThrough(frameWatcher(listener));
    return new Response(watched, {
      status: response.status,
      statusText: response.statusText,
      headers: response.headers,
    });
  };
}

/**
 * gRPC-Web frame layout: [1 byte flags][4 bytes length, big-endian][length bytes payload].
 * flags & 0x80 = trailer frame (the call's status), not a message.
 * Chunks from the network can split a frame (even its header) anywhere,
 * so the parser keeps its state between chunks.
 */
function frameWatcher(listener: FrameListener): TransformStream<Uint8Array, Uint8Array> 
{
  const header = new Uint8Array(5);
  let headerFill = 0; // header bytes collected so far (0..5)
  let remaining = 0;  // payload bytes still missing for the current frame
  let total = 0;      // payload size of the current frame
  let isMessage = false;

  return new TransformStream<Uint8Array, Uint8Array>({
    transform(chunk, controller) {
      let i = 0;
      while (i < chunk.length) {
        if (remaining === 0) {
          // Reading a header
          const take = Math.min(5 - headerFill, chunk.length - i);
          header.set(chunk.subarray(i, i + take), headerFill);
          headerFill += take;
          i += take;

          if (headerFill === 5) {
            headerFill = 0;
            total = ((header[1] << 24) | (header[2] << 16) | (header[3] << 8) | header[4]) >>> 0;
            remaining = total;
            isMessage = (header[0] & 0x80) === 0;
            if (isMessage) listener.onFrameStart(total);
          }
        } else {
          // Reading payload
          const take = Math.min(remaining, chunk.length - i);
          remaining -= take;
          i += take;
          if (isMessage) listener.onFrameProgress(total - remaining, total);
        }
      }
      controller.enqueue(chunk); // pass the bytes on, unchanged
    },
  });
}