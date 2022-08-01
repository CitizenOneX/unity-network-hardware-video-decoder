/*
 * Unity Network Hardware Video Decoder
 * 
 * Copyright 2019 (C) Bartosz Meglicki <meglickib@gmail.com>
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 */

using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

public class VideoDepthAudioRenderer : MonoBehaviour
{
	private string hardwareDepth = "cuda";
	private string hardwareTexture = "cuda";
	private string codecDepth = "hevc_cuvid";
	private string codecTexture = "hevc_cuvid";
	private string deviceDepth = "";
	private string deviceTexture = "";
	private int widthDepth = 0;     // automatically detected at runtime
	private int widthTexture = 0;   // aligned streams match resolutions
	private int heightDepth = 0;
	private int heightTexture = 0;
	private string pixel_formatDepth = "p010le";
	private string pixel_formatTexture = "nv12";

	private string ip = "";
	private ushort port = 9766;
	private int timeout_ms = 5000; // 5 seconds to wait for sender to start, otherwise won't recover. If set to 0 (no timeout), Unity can't seem to stop the process without hanging/being killed

	private IntPtr unhvd;

	private UNHVD.unhvd_frame[] frame = new UNHVD.unhvd_frame[]
	{
		new UNHVD.unhvd_frame{ data=new System.IntPtr[3], linesize=new int[3] }, // depth
		new UNHVD.unhvd_frame{ data=new System.IntPtr[3], linesize=new int[3] }, // color
		new UNHVD.unhvd_frame{ data=new System.IntPtr[3], linesize=new int[3] }  // aux (raw PCM audio)
	};

	private Texture2D colorTexture;

	private const int audioSampleRate = 22050;
	private const int audioSampleBufferLength = 8192;
	private int position = 0;
	private CircularBuffer<float> audioBuffer;


	private int frameNumber = 0; // TODO just testing the number of times things are called

	void Awake()
	{
		// trim down the debug messages to not include stack traces
		Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);

		UNHVD.unhvd_net_config net_config = new UNHVD.unhvd_net_config { ip = this.ip, port = this.port, timeout_ms = this.timeout_ms };

		UNHVD.unhvd_hw_config[] hw_config = new UNHVD.unhvd_hw_config[]
		{
			new UNHVD.unhvd_hw_config{hardware=this.hardwareDepth, codec=this.codecDepth, device=this.deviceDepth, pixel_format=this.pixel_formatDepth, width=this.widthDepth, height=this.heightDepth, profile=0},
			new UNHVD.unhvd_hw_config{hardware=this.hardwareTexture, codec=this.codecTexture, device=this.deviceTexture, pixel_format=this.pixel_formatTexture, width=this.widthTexture, height=this.heightTexture, profile=0}
		};

		// TODO use depth config if we need to use unprojection code
		//UNHVD.unhvd_depth_config dc = new UNHVD.unhvd_depth_config { ppx = 319.809f, ppy = 236.507f, fx = 606.767f, fy = 607.194f, depth_unit = 0.0001f, min_margin = 0.19f, max_margin = 0.01f };

		unhvd = UNHVD.unhvd_init(ref net_config, hw_config, hw_config.Length, 1, IntPtr.Zero); //ref dc);

		if (unhvd == IntPtr.Zero)
		{
			Debug.Log ("failed to initialize UNHVD");
			gameObject.SetActive (false);
		}

		//flip the texture mapping upside down
		Vector2[] uv = GetComponent<MeshFilter>().mesh.uv;
		for (int i = 0; i < uv.Length; ++i)
			uv [i][1] = -uv [i][1];
		GetComponent<MeshFilter> ().mesh.uv = uv;
	}

	void Start()
	{
		// create a buffer to hold received audio data
		audioBuffer = new CircularBuffer<float>(audioSampleBufferLength);

		// create a streaming audio clip that will call back every time it needs new samples
		AudioSource aud = GetComponent<AudioSource>();
		aud.clip = AudioClip.Create("AudioStream", audioSampleBufferLength, 1, audioSampleRate, true, OnAudioRead, OnAudioSetPosition);
		aud.Play();
	}

    private void OnAudioSetPosition(int newPosition)
    {
		Debug.Log("OnAudioSetPosition called newPosition=" + newPosition);
		// only seems to be called to set position to 0 (i.e. when the looping sample loops again)
		this.position = newPosition;
	}

    private void OnAudioRead(float[] data)
    {
		// actually I just want to copy data.Length samples into data (and increment position by data.Length?)
		// data.Length varies when it's called, either 4096 or sometimes twice with smaller values that add to 4096,
		// and sometimes a loop inbetween (i.e. SetPosition is called with position = 0)
		// do I always want to copy the "latest" data.Length samples from my (ringbuffer?)
		// of streamed audio? What if it asks for another 4096 right away? I guess this sets the latency, the size of this Clip,
		// and I run the risk of buffer underruns if I make this too small

		// why don't I get the sender to stream 4096 samples at a time, then when OnAudioRead is called,
		// if length is 4096, copy the last frame[2].data and zero the offset; if length < 4096 and the offset is zero,
		// copy the first data.Length samples; if length < 4096 and the offset is non-zero, send data.Length samples from offset=offset and
		// increment offset by data.Length
		// Actually it might only be necessary to zero the offset in OnAudioSetPosition, otherwise increment it by data.Length in OnAudioRead
		// provided the buffer length here is the same as the buffer length being sent
		// Still wondering about whether access to frame[2] needs to be synchronised (or just copy the data pointers and size? will the memory be freed before my use?)
		// Or whether I need to take a full copy of the frame data as it comes in (it is quite nice if it's a fixed length then)
		// TODO need to check on Android whether it requests 4096 each time or some other amount
		Debug.Log("OnAudioRead called data.Length=" + data.Length);

		int readCount = audioBuffer.Read(data, 0, data.Length);
		
		if (readCount < data.Length)
		{
			// clear out the remainder of the array (silence) for buffer underrun?
			Array.Clear(data, readCount, data.Length - readCount);
			Debug.Log("OnAudioRead insufficient data available: " + readCount);
		}


		/*
		// if the audio data frames are well-formed I shouldn't need to check all of these things
		if (frame != null && frame[2].data != null && frame[2].data[0] != null && frame[2].linesize != null && frame[2].linesize[0] != 0)
        {
			unsafe
			{
				Debug.Log("Frame linesize=" + frame[2].linesize[0] + ", Position=" + position);
				// Cast the data to a float native array (up to 4096 samples)
				// If prior calls have asked for less than a full frame, then we'll have an offset pointer into the native frame data
				// Take data.Length samples from the position of the offset
				// Then we need to convert the shorts to floats (promote to float and divide by 2^15)
				// TODO Check if little-endian or big-endian (not sure, might be different on Windows and Android too)
				// For what it's worth, on Windows it looks like interpreting the data[0] block as (signed) shorts is right - good endianness!
				int positionInBytes = position * UnsafeUtility.SizeOf<float>();
				NativeArray<float> audioSamples = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<float>(
					IntPtr.Add(frame[2].data[0], positionInBytes).ToPointer(), Math.Min(data.Length, frame[2].linesize[0] - positionInBytes), Allocator.None);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
				NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref audioSamples, AtomicSafetyHandle.Create());
#endif

				// copy the data back to the managed array
				// can't use CopyTo if the lengths aren't the same
				if (audioSamples.Length == data.Length)
				{
					audioSamples.CopyTo(data);
				}
				else
                {
					// copy the sample data that we do have
					int byteLength = audioSamples.Length * UnsafeUtility.SizeOf<float>();
					void* managedBuffer = UnsafeUtility.AddressOf(ref data[0]);
					void* nativeBuffer = audioSamples.GetUnsafePtr();
					UnsafeUtility.MemCpy(managedBuffer, nativeBuffer, byteLength);

					if (audioSamples.Length < data.Length)
					{
						// clear out the remainder of the array (silence) for buffer underrun?
						Array.Clear(data, audioSamples.Length, data.Length - audioSamples.Length);
						Debug.Log("OnAudioRead insufficient data available: " + audioSamples.Length);
					}
				}

				// advance the offset position in the incoming native audio frame by the amount we just copied over
				// so that on the next request from Unity we can send the next part
				position += data.Length; // TODO - or audioSamples.Length?! Probably don't want to fall behind
			}
		}
        else
        {
			// hopefully data[] is zeroes or whatever it was before, or something sensible...?
			// but if not
			Array.Clear(data, 0, data.Length);
		}
		*/
	}

	void OnDestroy()
	{
		UNHVD.unhvd_close (unhvd);
	}

	private void AdaptTexture()
	{
		if (colorTexture == null || colorTexture.width != frame[1].width || colorTexture.height != frame[1].height)
		{
			Debug.Log(string.Format("Texture plane format: {0} linesizes: ({1}, {2}, {3})", frame[1].format, frame[1].linesize[0], frame[1].linesize[1], frame[1].linesize[2]));
			colorTexture = new Texture2D (frame[1].width, frame[1].height, TextureFormat.RGB24, false);
			GetComponent<Renderer> ().material.mainTexture = colorTexture;
		}
	}

	// Update is called once per frame
	void LateUpdate()
	{
		if (UNHVD.unhvd_get_frame_begin(unhvd, frame) == 0)
		{
			Debug.Log("----" + ++frameNumber);
			// if a color texture frame is present, ensure existing color Texture2D
			// matches width/height then copy data over
			// looks like data is hanging around in data[0] so I'll check linesizes are non-zero too
			if (frame[1].data != null && frame[1].data[0] != null && frame[1].linesize != null && frame[1].linesize[0] > 0)
			{
				AdaptTexture();
				var data = colorTexture.GetRawTextureData<byte>();
				int pixels = frame[1].width * frame[1].height;
				unsafe
				{
					for (int index = 0; index < pixels; index++)
					{
						// NV12 has a Y plane and a UV-interleaved plane
						// load the w x h luma first as grayscale
						byte Y = ((byte*)frame[1].data[0])[index];
						data[3 * index] = Y;
						data[3 * index + 1] = Y;
						data[3 * index + 2] = Y;
					}
				}
				colorTexture.Apply(false);
			}

			// if audio is present...
			// TODO maybe copy to a threadsafe ringbuffer so the AudioClip can pull the data as it needs to
			if (frame[2].data != null && frame[2].data[0] != null && frame[2].linesize != null && frame[2].linesize[0] > 0)
            {
				unsafe
                {
					// cast the incoming audio frame to a float array
					NativeArray<float> audioSamples = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<float>(
						frame[2].data[0].ToPointer(), 
						Math.Min(audioSampleBufferLength * UnsafeUtility.SizeOf<float>(), frame[2].linesize[0]), 
						Allocator.None);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
					NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref audioSamples, AtomicSafetyHandle.Create());
#endif

					// copy the float array to the circular audio buffer, up to the full capacity (i.e. overwrite old samples if necessary)
					audioBuffer.Write(audioSamples, 0, audioBuffer.Capacity);
				}
			}
		}

		if (UNHVD.unhvd_get_frame_end(unhvd) != 0)
			Debug.LogWarning("Failed to get UNHVD frame data");
	}
}
