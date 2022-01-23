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

public class VideoRenderer : MonoBehaviour
{
	private string hardware = "cuda";
	private string codec = "hevc_cuvid";
	private string device = "";
	private string pixel_format = "yuv420p";
	private string ip = "";
	private ushort port = 9766;


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

		//flip the texture mapping upside down
		Vector2[] uv = GetComponent<MeshFilter>().mesh.uv;
		for (int i = 0; i < uv.Length; ++i)
			uv [i][1] = -uv [i][1];
		GetComponent<MeshFilter> ().mesh.uv = uv;
	}
	void OnDestroy()
	{
		UNHVD.unhvd_close (unhvd);
	}

	private void AdaptTexture()
	{
		if(videoTexture== null || videoTexture.width != frame.width || videoTexture.height != frame.height)
		{
			videoTexture = new Texture2D (frame.width, frame.height, TextureFormat.RGB24, false);
			GetComponent<Renderer> ().material.mainTexture = videoTexture;
		}
	}

	// Update is called once per frame
	void LateUpdate ()
	{
		if (UNHVD.unhvd_get_frame_begin(unhvd, ref frame) == 0)
		{
			AdaptTexture ();
			var data = videoTexture.GetRawTextureData<byte>();
			int pixels = frame.width * frame.height;
			unsafe
			{
				for (int index = 0; index < pixels; index++)
				{
					// NV12 has Y and UV interleaved planes
					// load the w x h luma first
					byte Y = ((byte*)frame.data[0])[index];
					data[3 * index] = Y;
					data[3 * index + 1] = Y;
					data[3 * index + 2] = Y;
				}
			}
			videoTexture.Apply (false);
		}

		if (UNHVD.unhvd_get_frame_end (unhvd) != 0)
			Debug.LogWarning ("Failed to get UNHVD frame data");
	}
}
