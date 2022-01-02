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
using UnityEngine;
using UnityEngine.UI; //RawImage

public class RawImageVideoRenderer : MonoBehaviour
{
	public string hardware = "cuda";
	public string codec = "h264";
	public string device = "";
	public string pixel_format = "rgb0";
	public string ip = "";
	public ushort port = 9766;

	private IntPtr unhvd;
	private UNHVD.unhvd_frame frame = new UNHVD.unhvd_frame{ data=new System.IntPtr[3], linesize=new int[3] };
	private Texture2D videoTexture;

	void Awake()
	{
		UNHVD.unhvd_hw_config hw_config = new UNHVD.unhvd_hw_config{hardware=this.hardware, codec=this.codec, device=this.device, pixel_format=this.pixel_format, width=0, height=0, profile=0};
		UNHVD.unhvd_net_config net_config = new UNHVD.unhvd_net_config{ip=this.ip, port=this.port, timeout_ms=500 };

		unhvd=UNHVD.unhvd_init (ref net_config, ref hw_config);

		if (unhvd == IntPtr.Zero)
		{
			Debug.Log ("failed to initialize UNHVD");
			gameObject.SetActive (false);
		}
			
	}
	void OnDestroy()
	{
		UNHVD.unhvd_close (unhvd);
	}

	private void AdaptTexture()
	{
		if(videoTexture== null || videoTexture.width != frame.width || videoTexture.height != frame.height)
		{
			videoTexture = new Texture2D (frame.width, frame.height, TextureFormat.RGBA32, false);
			GetComponent<RawImage> ().texture = videoTexture;
		}
	}

	// Update is called once per frame
	unsafe void LateUpdate ()
	{
		if (UNHVD.unhvd_get_frame_begin(unhvd, ref frame) == 0)
		{
			AdaptTexture();

			// RGB0 has one plane, w x h x 4 bytes per pixel
			videoTexture.LoadRawTextureData(frame.data[0], frame.width * frame.height * 4); 

			//else
			//{
			//	// FIXME planar format not working yet (if removed, LateUpdate doesn't need to be marked unsafe)
			//	// NV12/YUY2 has two planes, Y and U/V-interleaved
			//	int pixels = frame.width * frame.height;
			//	byte[] rawData = videoTexture.GetRawTextureData();
			//	fixed (byte* rawDataPtr = rawData)
			//	{
			//		System.Buffer.MemoryCopy(frame.data[0].ToPointer(), rawDataPtr, rawData.Length, pixels);
			//		System.Buffer.MemoryCopy(frame.data[1].ToPointer(), rawDataPtr + pixels, rawData.Length, pixels);
			//	}

			//	//videoTexture.LoadRawTextureData(frame.data[0], frame.width * frame.height); 
			//}

			videoTexture.Apply (false);
		}

		if (UNHVD.unhvd_get_frame_end (unhvd) != 0)
			Debug.LogWarning ("Failed to get UNHVD frame data");
	}
}