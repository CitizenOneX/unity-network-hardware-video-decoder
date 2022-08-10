/*
 * Unity Network Hardware Video Decoder
 * 
 * Copyright 2020 (C) Bartosz Meglicki <meglickib@gmail.com>
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 *
 */

using System;
using UnityEngine;

public class GPUPointCloudRenderer : MonoBehaviour
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

	private IntPtr unhvd;

	private UNHVD.unhvd_frame[] frame = new UNHVD.unhvd_frame[]
	{
		new UNHVD.unhvd_frame{ data=new System.IntPtr[3], linesize=new int[3] }, // depth
		new UNHVD.unhvd_frame{ data=new System.IntPtr[3], linesize=new int[3] } // color
		//new UNHVD.unhvd_frame{ data=new System.IntPtr[1], linesize=new int[3] }  // aux (raw PCM audio)
	};

	private Texture2D depthTexture; //uint16 depth map filled with data from native side
	private Texture2D colorTextureY; // NV12 Y-color plane filled with data from native side to be combined in the shader
	private Texture2D colorTextureUV; // NV12 interleaved UV (width/2)*2, height/2, needs to be unpacked from single UV plane the shader

	private ComputeBuffer vertexBuffer; // float4,float4 (vertex position, color) AppendStructuredBuffer in compute shader
	//private GraphicsBuffer indexBuffer;  // uint (indices of triangle vertices), AppendStructuredBuffer in compute shader
	private ComputeBuffer argsBuffer;   // indirect rendering args

	public ComputeShader unprojectionShader;
	public Shader pointCloudShader;

	private Material material;

	void Awake()
	{
		// trim down the debug messages to not include stack traces
		Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);

		//Application.targetFrameRate = 30;
		QualitySettings.vSyncCount = 0;

		UNHVD.unhvd_net_config net_config = new UNHVD.unhvd_net_config { ip = this.ip, port = this.port, timeout_ms = 500 };
		UNHVD.unhvd_hw_config[] hw_config = new UNHVD.unhvd_hw_config[]
		{
			new UNHVD.unhvd_hw_config{hardware=this.hardwareDepth, codec=this.codecDepth, device=this.deviceDepth, pixel_format=this.pixel_formatDepth, width=this.widthDepth, height=this.heightDepth, profile=0},
			new UNHVD.unhvd_hw_config{hardware=this.hardwareTexture, codec=this.codecTexture, device=this.deviceTexture, pixel_format=this.pixel_formatTexture, width=this.widthTexture, height=this.heightTexture, profile=0}
		};

		unhvd = UNHVD.unhvd_init(ref net_config, hw_config, hw_config.Length, 0, IntPtr.Zero); // TODO add the aux channel here as well

		if (unhvd == IntPtr.Zero)
		{
			Debug.Log("failed to initialize UNHVD");
			gameObject.SetActive(false);
			return;
		}

		// Compute Shader Setup
		argsBuffer = new ComputeBuffer(4, sizeof(int), ComputeBufferType.IndirectArguments);
		//vertex count per instance, instance count, start vertex location, start instance location
		//position 0 will be overwritten with index buffer count after shader runs
		//see https://docs.unity3d.com/2019.4/Documentation/ScriptReference/Graphics.DrawProceduralIndirectNow.html
		argsBuffer.SetData(new int[] { 0, 1, 0, 0 });

		//For depth config explanation see:
		//https://github.com/bmegli/unity-network-hardware-video-decoder/wiki/Point-clouds-configuration

		//For D435 at 848x480 the MinZ is ~16.8cm, in our result unit min_margin is 0.168
		//max_margin is arbitrarilly set
		//DepthConfig dc = new DepthConfig {ppx = 421.353f, ppy=240.93f, fx=426.768f, fy=426.768f, depth_unit = 0.0001f, min_margin = 0.168f, max_margin = 0.01f};
		//DepthConfig dc = new DepthConfig {ppx = 421.353f, ppy=240.93f, fx=426.768f, fy=426.768f, depth_unit = 0.0000390625f, min_margin = 0.168f, max_margin = 0.01f};

		//sample config for D455 848x480 with depth units resulting in 2.5 mm precision and 2.5575 m range, MinZ at 848x480 is 350 mm, for depth, depth + ir, depth aligned color
		//DepthConfig dc = new DepthConfig{ppx = 426.33f, ppy=239.446f, fx=422.768f, fy=422.768f, depth_unit = 0.0000390625f, min_margin = 0.35f, max_margin = 0.01f};

		// TODO work out what to do about the depth_unit *60 hack
		//sample config for L515 320x240 with depth units resulting in 6.4 mm precision and 6.5472 m range (alignment to depth)
		DepthConfig dc = new DepthConfig { ppx = 168.805f, ppy = 125.068f, fx = 229.699f, fy = 230.305f, depth_unit = 0.0001f, min_margin = 0.19f, max_margin = 0.01f };
		//sample config for L515 640x480 with depth units resulting in 2.5 mm precision and 2.5575 m range (alignment to depth)
		//?DepthConfig dc = new DepthConfig { ppx = 358.781f, ppy = 246.297f, fx = 470.941f, fy = 470.762f, depth_unit = 0.0000390625f, min_margin = 0.19f, max_margin = 0.01f };
		//DepthConfig dc = new DepthConfig { ppx = 319.809f, ppy = 236.507f, fx = 606.767f, fy = 607.194f, depth_unit = 0.0000390625f, min_margin = 0.19f, max_margin = 0.01f };

		SetDepthConfig(dc);
	}

	void SetDepthConfig(DepthConfig dc)
	{
		//The depth texture values will be normalied in the shader, 16 bit to [0, 1]
		float maxDistance = dc.depth_unit * 0xffff;
		//Normalize also valid ranges
		float minValidDistance = dc.min_margin / maxDistance;
		//Our depth encoding uses P010LE format for depth texture which uses 10 MSB of 16 bits (0xffc0 is 10 "1" and 6 "0")
		float maxValidDistance = (dc.depth_unit * 0xffc0 - dc.max_margin) / maxDistance;
		//The multiplier renormalizes [0, 1] to real world units again and is part of unprojection
		float[] unprojectionMultiplier = { maxDistance / dc.fx, maxDistance / dc.fy, maxDistance };

		unprojectionShader.SetFloats("UnprojectionMultiplier", unprojectionMultiplier);
		unprojectionShader.SetFloat("PPX", dc.ppx);
		unprojectionShader.SetFloat("PPY", dc.ppy);
		unprojectionShader.SetFloat("MinDistance", minValidDistance);
		unprojectionShader.SetFloat("MaxDistance", maxValidDistance);
	}

	void OnDestroy()
	{
		UNHVD.unhvd_close(unhvd);

		if (vertexBuffer != null)
			vertexBuffer.Release();

		//if (indexBuffer != null)
		//	indexBuffer.Release();

		if (argsBuffer != null)
			argsBuffer.Release();

		if (material != null)
		{
			if (Application.isPlaying)
				Destroy(material);
			else
				DestroyImmediate(material);
		}
	}

	void LateUpdate()
	{
		bool updateNeeded = false;

		if (UNHVD.unhvd_get_frame_begin(unhvd, frame) == 0)
			updateNeeded = PrepareTextures();

		if (UNHVD.unhvd_get_frame_end(unhvd) != 0)
			Debug.LogWarning("Failed to get UNHVD frame data");

		if (!updateNeeded)
			return;

		vertexBuffer.SetCounterValue(0);
		//indexBuffer.SetCounterValue(0);
		// create width/8 * height/8 thread groups, e.g. 40*30 = 1200 groups for 320x240
		// each thread group will be set up as 8x8 threads
		unprojectionShader.Dispatch(0, frame[0].width / 8, frame[0].height / 8, 1);
		ComputeBuffer.CopyCount(vertexBuffer, argsBuffer, 0);
		//GraphicsBuffer.CopyCount(indexBuffer, argsBuffer, 0);
		// TODO check if I should make a triangle index struct and append indices in threes, and 
		// perform CopyCount as number of triangles as instances rather than one instance of thousands
		// of indices. If so, change CopyCount offset in bytes to 4 maybe to write the second int in the buffer.
	}

	private bool PrepareTextures()
	{
		if (frame[0].data[0] == IntPtr.Zero)
			return false;

		Adapt();

		depthTexture.LoadRawTextureData(frame[0].data[0], frame[0].linesize[0] * frame[0].height);
		depthTexture.Apply(false);

		// if there's also a color texture frame
		if (frame[1].data[0] != IntPtr.Zero)
		{
			// All the NV12 texture data comes in frame 1, in 2 planes (Y, full width/ half height UV interleaved)
			int yplane_size = frame[1].linesize[0] * frame[1].height;
			colorTextureY.LoadRawTextureData(frame[1].data[0], yplane_size);
			colorTextureUV.LoadRawTextureData(frame[1].data[1], yplane_size / 2);
			colorTextureY.Apply(false);
			colorTextureUV.Apply(false);
		}

		return true;
	}

	private void Adapt()
	{
		//adapt to incoming stream if something changed
		if (depthTexture == null || depthTexture.width != frame[0].width || depthTexture.height != frame[0].height)
		{
			depthTexture = new Texture2D(frame[0].width, frame[0].height, TextureFormat.R16, false);
			unprojectionShader.SetTexture(0, "depthTexture", depthTexture);

			// vertex buffer holds position(float4) and color(float4)
			//vertexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Vertex | GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.Append, frame[0].width * frame[0].height, 2 * sizeof(float) * 4);
			vertexBuffer = new ComputeBuffer(frame[0].width * frame[0].height, 2 * sizeof(float) * 4, ComputeBufferType.Append);
			unprojectionShader.SetBuffer(0, "vertices", vertexBuffer);

			// index buffer holds triangle indices (up to width-1 * height-1 * 2, element size 3 * uint)
			// but 12 is an invalid stride, so create it as individual 4-byte uints and see if we can have it 
			// structured in the compute shader
			//indexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Index, (frame[0].width - 1) * (frame[0].height - 1) * 2 * 3, sizeof(uint));
			// FIXME just set 3 indices from a central triangle for starters
			//indexBuffer.SetData(new uint[] { 120 * 320 + 160, 121 * 320 + 160, 120 * 320 + 159 }); // y increases downwards?
			//indexBuffer.SetData(new uint[] { 120 * 320 + 160, 121 * 320 + 160, 121 * 320 + 159 }); // y increases upwards?
			//unprojectionShader.SetBuffer(0, "indices", indexBuffer);
		}

		if (colorTextureY == null || colorTextureY.width != frame[1].width || colorTextureY.height != frame[1].height)
		{
			if (frame[1].data[0] != IntPtr.Zero)
			{
				Debug.Log(string.Format("Texture data: format:{0}, planes({1}, {2}, {3}), sizes:({4}, {5}, {6})", frame[1].format, frame[1].data[0], frame[1].data[1], frame[1].data[2], frame[1].linesize[0], frame[1].linesize[1], frame[1].linesize[2]));
				colorTextureY = new Texture2D(frame[1].width, frame[1].height, TextureFormat.R8, false);
				colorTextureUV = new Texture2D(frame[1].width, frame[1].height / 2, TextureFormat.R8, false);
			}
			else
			{   //in case only depth data is coming prepare dummy color textures
				colorTextureY = new Texture2D(frame[0].width, frame[0].height, TextureFormat.R8, false);
				byte[] data = new byte[frame[0].width * frame[0].height];
				for (int i = 0; i < data.Length; i++)
					data[i] = 0xFF;
				colorTextureY.SetPixelData(data, 0, 0);
				colorTextureY.Apply();

				colorTextureUV = new Texture2D(frame[0].width, frame[0].height / 2, TextureFormat.R8, false);
				data = new byte[frame[0].width * frame[0].height / 2];
				for (int i = 0; i < data.Length; i++)
					data[i] = 0x80;
				colorTextureUV.SetPixelData(data, 0, 0);
				colorTextureUV.Apply();
			}
			unprojectionShader.SetTexture(0, "colorTextureY", colorTextureY);
			unprojectionShader.SetTexture(0, "colorTextureUV", colorTextureUV);
		}
	}

	void OnRenderObject()
	{
		if (vertexBuffer == null)
			return;

		if (material == null)
		{
            material = new Material(pointCloudShader)
            {
                hideFlags = HideFlags.DontSave
            };
        }

		material.SetPass(0);
		material.SetMatrix("transform", transform.localToWorldMatrix);
		material.SetBuffer("vertices", vertexBuffer);
		//material.SetBuffer("indices", indexBuffer);

		//Graphics.DrawProceduralIndirectNow(MeshTopology.Triangles, indexBuffer, argsBuffer);
		Graphics.DrawProceduralIndirectNow(MeshTopology.Points, argsBuffer);
	}
}
