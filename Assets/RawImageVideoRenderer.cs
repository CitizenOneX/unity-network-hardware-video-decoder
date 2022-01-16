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
	private string hardware = "";
	private string codec = "hevc";
	private string device = "";
	private string pixel_format = "yuv420p";
	private TextureFormat texture_format = TextureFormat.RGBA32; // needs to agree with pixel format
	private string ip = "";
	private ushort port = 9766;

	private IntPtr unhvd;
	private UNHVD.unhvd_frame frame = new UNHVD.unhvd_frame{ data=new System.IntPtr[3], linesize=new int[3] };
	private Texture2D videoTexture;

	private bool DEPTH_AND_TEXTURE_STREAMS = true;


	void Awake()
	{
		UNHVD.unhvd_net_config net_config = new UNHVD.unhvd_net_config{ip=this.ip, port=this.port, timeout_ms=500 };
		Debug.Log(String.Format("netconfig: {0}, {1}", net_config.ip, net_config.port.ToString()));

		// TODO just testing the texture stream of a depth/texture pair of streams
		if (DEPTH_AND_TEXTURE_STREAMS) // if depth and texture streams provided
		{
			UNHVD.unhvd_hw_config[] hw_config =
			{
				new UNHVD.unhvd_hw_config{hardware=this.hardware, codec=this.codec, device=this.device, pixel_format=this.pixel_format, width=0, height=0, profile=2}, // profile FF_PROFILE_HEVC_MAIN_10
				new UNHVD.unhvd_hw_config{hardware=this.hardware, codec=this.codec, device=this.device, pixel_format=this.pixel_format, width=0, height=0, profile=1} // FF_PROFILE_HEVC_MAIN
			};
			Debug.Log(String.Format("hwconfig[0]: {0}, {1}, {2}, {3}", hw_config[0].hardware, hw_config[0].codec, hw_config[0].device, hw_config[0].pixel_format));
			Debug.Log(String.Format("hwconfig[1]: {0}, {1}, {2}, {3}", hw_config[1].hardware, hw_config[1].codec, hw_config[1].device, hw_config[1].pixel_format));

			//sample config for L515 320x240 with depth units resulting in 6.4 mm precision and 6.5472 m range (alignment to depth)
			DepthConfig dc = new DepthConfig { ppx = 168.805f, ppy = 125.068f, fx = 229.699f, fy = 230.305f, depth_unit = 0.0001f * 60, min_margin = 0.19f, max_margin = 0.01f };
			UNHVD.unhvd_depth_config depth_config = new UNHVD.unhvd_depth_config { ppx = dc.ppx, ppy = dc.ppy, fx = dc.fx, fy = dc.fy, depth_unit = dc.depth_unit, min_margin = dc.min_margin, max_margin = dc.max_margin };

			unhvd = UNHVD.unhvd_init(ref net_config, hw_config, hw_config.Length, ref depth_config);
		}
		else // texture stream only
		{
			UNHVD.unhvd_hw_config hw_config = new UNHVD.unhvd_hw_config { hardware = this.hardware, codec = this.codec, device = this.device, pixel_format = this.pixel_format, width = 0, height = 0, profile = 0 };
			Debug.Log(String.Format("hwconfig[0]: {0}, {1}, {2}, {3}", hw_config.hardware, hw_config.codec, hw_config.device, hw_config.pixel_format));
			unhvd = UNHVD.unhvd_init(ref net_config, ref hw_config);
		}

		if (unhvd == IntPtr.Zero)
		{
			Debug.Log("failed to initialize UNHVD");
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
			Debug.Log(string.Format("AdaptTexture creating new Texture for Frame: {0}x{1}x{2}, planes:{3},{4},{5}", frame.width, frame.height, frame.format, frame.linesize[0], frame.linesize[1], frame.linesize[2]));
			videoTexture = new Texture2D(frame.width, frame.height, texture_format, false);
			GetComponent<RawImage> ().texture = videoTexture;
		}
	}

	// Update is called once per frame
	void LateUpdate()
	{
		if (DEPTH_AND_TEXTURE_STREAMS)
		{
			UNHVD.unhvd_frame[] frames = { new UNHVD.unhvd_frame { }, new UNHVD.unhvd_frame { }, new UNHVD.unhvd_frame { } };

			if (UNHVD.unhvd_get_frame_begin(unhvd, frames) == 0)
			{
				frame = frames[1]; // copy reference to texture frame (1)
				AdaptTexture();

				var data = videoTexture.GetRawTextureData<byte>();
				int pixels = frame.width * frame.height;
				unsafe
				{
					for (int index = 0; index < pixels; index++)
					{
						// YUV420P has Y, U and V planes
						// load the w x h luma first
						byte Y = ((byte*)frame.data[0])[index];
						data[4 * index] = Y;
						data[4 * index + 1] = Y;
						data[4 * index + 2] = Y;
						data[4 * index + 3] = 255;
					}
				}

				// upload to the GPU
				videoTexture.Apply();
			}
		}
		else
		{
			if (UNHVD.unhvd_get_frame_begin(unhvd, ref frame) == 0)
			{
				AdaptTexture();

				var data = videoTexture.GetRawTextureData<byte>();
				int pixels = frame.width * frame.height;
				unsafe
				{
					for (int index = 0; index < pixels; index++)
					{
						// YUV420P has Y, U and V planes
						// load the w x h luma first
						byte Y = ((byte*)frame.data[0])[index];
						data[4 * index] = Y;
						data[4 * index + 1] = Y;
						data[4 * index + 2] = Y;
						data[4 * index + 3] = 255;
					}
				}

				// upload to the GPU
				videoTexture.Apply();
			}
		}

		if (UNHVD.unhvd_get_frame_end (unhvd) != 0)
			Debug.LogWarning ("Failed to get UNHVD frame data");
	}
}