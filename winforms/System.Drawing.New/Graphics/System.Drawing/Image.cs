//
// System.Drawing.Image.cs
//
// Authors: 	Christian Meyer (Christian.Meyer@cs.tum.edu)
// 		Alexandre Pigolkine (pigolkine@gmx.de)
//		Jordi Mas i Hernandez (jordi@ximian.com)
//		Sanjay Gupta (gsanjay@novell.com)
//		Ravindra (rkumar@novell.com)
//		Sebastien Pouliot  <sebastien@ximian.com>
//
// Copyright (C) 2002 Ximian, Inc.  http://www.ximian.com
// Copyright (C) 2004, 2007 Novell, Inc (http://www.novell.com)
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using System;
using System.Runtime.Serialization;
using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;

namespace System.Drawing
{
[Serializable]
[ComVisible (true)]
[Editor ("System.Drawing.Design.ImageEditor, " + Consts.AssemblySystem_Drawing_Design, typeof (System.Drawing.Design.UITypeEditor))]
[TypeConverter (typeof(ImageConverter))]
[ImmutableObject (true)]
public abstract class Image : MarshalByRefObject, IDisposable , ICloneable, ISerializable 
{
	public delegate bool GetThumbnailImageAbort();
#if NET_2_0	
	private object tag;
#endif	
	
	internal IntPtr nativeObject = IntPtr.Zero;
	// when using MS GDI+ and IStream we must ensure the stream stays alive for all the life of the Image
	// http://groups.google.com/group/microsoft.public.win32.programmer.gdi/browse_thread/thread/4967097db1469a27/4d36385b83532126?lnk=st&q=IStream+gdi&rnum=3&hl=en#4d36385b83532126
	internal Stream stream;
	
	
	// constructor
	internal  Image()
	{	
	}
	
	internal Image (SerializationInfo info, StreamingContext context)
	{
		foreach (SerializationEntry serEnum in info) {
			if (String.Compare(serEnum.Name, "Data", true) == 0) {
				byte[] bytes = (byte[]) serEnum.Value;

				if (bytes != null) {
					MemoryStream ms = new MemoryStream (bytes);
					nativeObject = InitFromStream (ms);
					// under Win32 stream is owned by SD/GDI+ code
					if (GDIPlus.RunningOnWindows ())
						stream = ms;
				}
			}
		}
	}

	// FIXME - find out how metafiles (another decoder-only codec) are handled
	void ISerializable.GetObjectData (SerializationInfo si, StreamingContext context)
	{
		using (MemoryStream ms = new MemoryStream ()) {
			// Icon is a decoder-only codec
			if (RawFormat.Equals (ImageFormat.Icon)) {
				Save (ms, ImageFormat.Png);
			} else {
				Save (ms, RawFormat);
			}
			si.AddValue ("Data", ms.ToArray ());
		}
	}
    
	// public methods
	// static
	public static Image FromFile(string filename)
	{
		return FromFile (filename, false);
	}
	
	public static Image FromFile(string filename, bool useEmbeddedColorManagement)
	{
		IntPtr imagePtr;
		Status st;

		if (!File.Exists (filename))
			throw new FileNotFoundException (filename);

		if (useEmbeddedColorManagement)
			st = GDIPlus.GdipLoadImageFromFileICM (filename, out imagePtr);
		else
			st = GDIPlus.GdipLoadImageFromFile (filename, out imagePtr);
		GDIPlus.CheckStatus (st);

		return CreateFromHandle (imagePtr);
	}

	public static Bitmap FromHbitmap(IntPtr hbitmap)
	{
		return FromHbitmap (hbitmap, IntPtr.Zero);
	}

	public static Bitmap FromHbitmap(IntPtr hbitmap, IntPtr hpalette)
	{		
		IntPtr imagePtr;
		Status st;

		st = GDIPlus.GdipCreateBitmapFromHBITMAP (hbitmap, hpalette, out imagePtr);

		GDIPlus.CheckStatus (st);
		return new Bitmap (imagePtr);
	}

	// note: FromStream can return either a Bitmap or Metafile instance

	public static Image FromStream (Stream stream)
	{
		return LoadFromStream (stream, false);
	}

	[MonoLimitation ("useEmbeddedColorManagement  isn't supported.")]
	public static Image FromStream (Stream stream, bool useEmbeddedColorManagement)
	{
		return LoadFromStream (stream, false);
	}

	// See http://support.microsoft.com/default.aspx?scid=kb;en-us;831419 for performance discussion	
	[MonoLimitation ("useEmbeddedColorManagement  and validateImageData aren't supported.")]
	public static Image FromStream (Stream stream, bool useEmbeddedColorManagement, bool validateImageData)
	{
		return LoadFromStream (stream, false);
	}

	internal static Image LoadFromStream (Stream stream, bool keepAlive)
	{
		if (stream == null)
			throw new ArgumentNullException ("stream");

		Image img = CreateFromHandle (InitFromStream (stream));

		// Under Windows, we may need to keep a reference on the stream as long as the image is alive
		// (GDI+ seems to use a lazy loader)
		if (keepAlive && GDIPlus.RunningOnWindows ())
			img.stream = stream;

		return img;
	}

	internal static Image CreateFromHandle (IntPtr handle)
	{
		ImageType type;
		GDIPlus.CheckStatus (GDIPlus.GdipGetImageType (handle, out type));
		switch (type) {
		case ImageType.Bitmap:
			return new Bitmap (handle);
		case ImageType.Metafile:
			return new Metafile (handle);
		default:
			throw new NotSupportedException (Locale.GetText ("Unknown image type."));
		}
	}

	public static int GetPixelFormatSize(PixelFormat pixfmt)
	{
		int result = 0;
		switch (pixfmt) {
			case PixelFormat.Format16bppArgb1555:
			case PixelFormat.Format16bppGrayScale:
			case PixelFormat.Format16bppRgb555:
			case PixelFormat.Format16bppRgb565:
				result = 16;
				break;
			case PixelFormat.Format1bppIndexed:
				result = 1;
				break;
			case PixelFormat.Format24bppRgb:
				result = 24;
				break;
			case PixelFormat.Format32bppArgb:
			case PixelFormat.Format32bppPArgb:
			case PixelFormat.Format32bppRgb:
				result = 32;
				break;
			case PixelFormat.Format48bppRgb:
				result = 48;
				break;
			case PixelFormat.Format4bppIndexed:
				result = 4;
				break;
			case PixelFormat.Format64bppArgb:
			case PixelFormat.Format64bppPArgb:
				result = 64;
				break;
			case PixelFormat.Format8bppIndexed:
				result = 8;
				break;
		}
		return result;
	}

	public static bool IsAlphaPixelFormat(PixelFormat pixfmt)
	{
		bool result = false;
		switch (pixfmt) {
			case PixelFormat.Format16bppArgb1555:
			case PixelFormat.Format32bppArgb:
			case PixelFormat.Format32bppPArgb:
			case PixelFormat.Format64bppArgb:
			case PixelFormat.Format64bppPArgb:
				result = true;
				break;
			case PixelFormat.Format16bppGrayScale:
			case PixelFormat.Format16bppRgb555:
			case PixelFormat.Format16bppRgb565:
			case PixelFormat.Format1bppIndexed:
			case PixelFormat.Format24bppRgb:
			case PixelFormat.Format32bppRgb:
			case PixelFormat.Format48bppRgb:
			case PixelFormat.Format4bppIndexed:
			case PixelFormat.Format8bppIndexed:
				result = false;
				break;
		}
		return result;
	}
	
	public static bool IsCanonicalPixelFormat (PixelFormat pixfmt)
	{
		return ((pixfmt & PixelFormat.Canonical) != 0);
	}
	
	public static bool IsExtendedPixelFormat (PixelFormat pixfmt)
	{
		return ((pixfmt & PixelFormat.Extended) != 0);
	}

	internal static IntPtr InitFromStream (Stream stream)
	{
		if (stream == null)
			throw new ArgumentException ("stream");

		IntPtr imagePtr;
		Status st;
		
		// Seeking required
		if (!stream.CanSeek) {
			byte[] buffer = new byte[256];
			int index = 0;
			int count;

			do {
				if (buffer.Length < index + 256) {
					byte[] newBuffer = new byte[buffer.Length * 2];
					Array.Copy(buffer, newBuffer, buffer.Length);
					buffer = newBuffer;
				}
				count = stream.Read(buffer, index, 256);
				index += count;
			}
			while (count != 0);

			stream = new MemoryStream(buffer, 0, index);
		}

		if (GDIPlus.RunningOnUnix ()) {
			// Unix, with libgdiplus
			// We use a custom API for this, because there's no easy way
			// to get the Stream down to libgdiplus.  So, we wrap the stream
			// with a set of delegates.
			GDIPlus.GdiPlusStreamHelper sh = new GDIPlus.GdiPlusStreamHelper (stream, true);

			st = GDIPlus.GdipLoadImageFromDelegate_linux (sh.GetHeaderDelegate, sh.GetBytesDelegate,
				sh.PutBytesDelegate, sh.SeekDelegate, sh.CloseDelegate, sh.SizeDelegate, out imagePtr);
		} else {
			st = GDIPlus.GdipLoadImageFromStream (new ComIStreamWrapper (stream), out imagePtr);
		}

		return st == Status.Ok ? imagePtr : IntPtr.Zero;
	}

	// non-static	
	public RectangleF_ GetBounds (ref GraphicsUnit pageUnit)
	{	
		RectangleF_ source;			
		
		Status status = GDIPlus.GdipGetImageBounds (nativeObject, out source, ref pageUnit);
		GDIPlus.CheckStatus (status);		
		
		return source;
	}
	
	public EncoderParameters GetEncoderParameterList(Guid encoder)
	{
		Status status;
		uint sz;

		status = GDIPlus.GdipGetEncoderParameterListSize (nativeObject, ref encoder, out sz);
		GDIPlus.CheckStatus (status);

		IntPtr rawEPList = Marshal.AllocHGlobal ((int) sz);
		EncoderParameters eps;

		try {
			status = GDIPlus.GdipGetEncoderParameterList (nativeObject, ref encoder, sz, rawEPList);
			eps = EncoderParameters.FromNativePtr (rawEPList);
			GDIPlus.CheckStatus (status);
		}
		finally {
			Marshal.FreeHGlobal (rawEPList);
		}

		return eps;
	}
	
	public int GetFrameCount (FrameDimension dimension)
	{
		uint count;
		Guid guid = dimension.Guid;

		Status status = GDIPlus.GdipImageGetFrameCount (nativeObject, ref guid, out count); 
		GDIPlus.CheckStatus (status);
		
		return (int) count;
	}
	
	public PropertyItem GetPropertyItem(int propid)
	{
		int propSize;
		IntPtr property;
		PropertyItem item = new PropertyItem ();
		GdipPropertyItem gdipProperty = new GdipPropertyItem ();
		Status status;
			
		status = GDIPlus.GdipGetPropertyItemSize (nativeObject, propid, 
									out propSize);
		GDIPlus.CheckStatus (status);

		/* Get PropertyItem */
		property = Marshal.AllocHGlobal (propSize);
		try {
			status = GDIPlus.GdipGetPropertyItem (nativeObject, propid, propSize, property);
			GDIPlus.CheckStatus (status);
			gdipProperty = (GdipPropertyItem) Marshal.PtrToStructure (property, 
								typeof (GdipPropertyItem));						
			GdipPropertyItem.MarshalTo (gdipProperty, item);
		}
		finally {
			Marshal.FreeHGlobal (property);
		}
		return item;
	}
	
	public Image GetThumbnailImage (int thumbWidth, int thumbHeight, Image.GetThumbnailImageAbort callback, IntPtr callbackData)
	{
		if ((thumbWidth <= 0) || (thumbHeight <= 0))
			throw new OutOfMemoryException ("Invalid thumbnail size");

		Image ThumbNail = new Bitmap (thumbWidth, thumbHeight);

		using (Graphics g = Graphics.FromImage (ThumbNail)) {
			Status status = GDIPlus.GdipDrawImageRectRectI (g.nativeObject, nativeObject,
				0, 0, thumbWidth, thumbHeight,
				0, 0, this.Width, this.Height,
				GraphicsUnit.Pixel, IntPtr.Zero, null, IntPtr.Zero);

			GDIPlus.CheckStatus (status);
		}

		return ThumbNail;
	}
	
	
	public void RemovePropertyItem (int propid)
	{		
		Status status = GDIPlus.GdipRemovePropertyItem (nativeObject, propid);
		GDIPlus.CheckStatus (status);					
	}	
	
	public void RotateFlip (RotateFlipType rotateFlipType)
	{			
		Status status = GDIPlus.GdipImageRotateFlip (nativeObject, rotateFlipType);
		GDIPlus.CheckStatus (status);				
	}

	internal ImageCodecInfo findEncoderForFormat (ImageFormat format)
	{
		ImageCodecInfo[] encoders = ImageCodecInfo.GetImageEncoders();			
		ImageCodecInfo encoder = null;
		
		if (format.Guid.Equals (ImageFormat.MemoryBmp.Guid))
			format = ImageFormat.Png;
	
		/* Look for the right encoder for our format*/
		for (int i = 0; i < encoders.Length; i++) {
			if (encoders[i].FormatID.Equals (format.Guid)) {
				encoder = encoders[i];
				break;
			}			
		}

		return encoder;
	}

	public void Save (string filename)
	{
		Save (filename, RawFormat);
	}

	public void Save(string filename, ImageFormat format) 
	{
		ImageCodecInfo encoder = findEncoderForFormat (format);
		if (encoder == null) {
			// second chance
			encoder = findEncoderForFormat (RawFormat);
			if (encoder == null) {
				string msg = Locale.GetText ("No codec available for saving format '{0}'.", format.Guid);
				throw new ArgumentException (msg, "format");
			}
		}
		Save (filename, encoder, null);
	}

	public void Save(string filename, ImageCodecInfo encoder, EncoderParameters encoderParams)
	{
		Status st;
		Guid guid = encoder.Clsid;

		if (encoderParams == null) {
			st = GDIPlus.GdipSaveImageToFile (nativeObject, filename, ref guid, IntPtr.Zero);
		} else {
			IntPtr nativeEncoderParams = encoderParams.ToNativePtr ();
			st = GDIPlus.GdipSaveImageToFile (nativeObject, filename, ref guid, nativeEncoderParams);
			Marshal.FreeHGlobal (nativeEncoderParams);
		}

		GDIPlus.CheckStatus (st);
	}

	public void Save (Stream stream, ImageFormat format)
	{
		ImageCodecInfo encoder = findEncoderForFormat (format);

		if (encoder == null)
			throw new ArgumentException ("No codec available for format:" + format.Guid);

		Save (stream, encoder, null);
	}

	public void Save(Stream stream, ImageCodecInfo encoder, EncoderParameters encoderParams)
	{
		Status st;
		IntPtr nativeEncoderParams;
		Guid guid = encoder.Clsid;

		if (encoderParams == null)
			nativeEncoderParams = IntPtr.Zero;
		else
			nativeEncoderParams = encoderParams.ToNativePtr ();

		try {
			if (GDIPlus.RunningOnUnix ()) {
				GDIPlus.GdiPlusStreamHelper sh = new GDIPlus.GdiPlusStreamHelper (stream, false);
				st = GDIPlus.GdipSaveImageToDelegate_linux (nativeObject, sh.GetBytesDelegate, sh.PutBytesDelegate,
					sh.SeekDelegate, sh.CloseDelegate, sh.SizeDelegate, ref guid, nativeEncoderParams);
			} else {
				st = GDIPlus.GdipSaveImageToStream (new HandleRef (this, nativeObject), 
					new ComIStreamWrapper (stream), ref guid, new HandleRef (encoderParams, nativeEncoderParams));
			}
		}
		finally {
			if (nativeEncoderParams != IntPtr.Zero)
				Marshal.FreeHGlobal (nativeEncoderParams);
		}
		
		GDIPlus.CheckStatus (st);		
	}
	
	public void SaveAdd (EncoderParameters encoderParams)
	{
		Status st;
		
		IntPtr nativeEncoderParams = encoderParams.ToNativePtr ();
		st = GDIPlus.GdipSaveAdd (nativeObject, nativeEncoderParams);
		Marshal.FreeHGlobal (nativeEncoderParams);
		GDIPlus.CheckStatus (st);
	}
		
	public void SaveAdd (Image image, EncoderParameters encoderParams)
	{
		Status st;
		
		IntPtr nativeEncoderParams = encoderParams.ToNativePtr ();
		st = GDIPlus.GdipSaveAddImage (nativeObject, image.NativeObject, nativeEncoderParams);
		Marshal.FreeHGlobal (nativeEncoderParams);
		GDIPlus.CheckStatus (st);
	}
		
	public int SelectActiveFrame(FrameDimension dimension, int frameIndex)
	{
		Guid guid = dimension.Guid;		
		Status st = GDIPlus.GdipImageSelectActiveFrame (nativeObject, ref guid, frameIndex);
		
		GDIPlus.CheckStatus (st);			
		
		return frameIndex;		
	}

	public void SetPropertyItem(PropertyItem propitem)
	{
		throw new NotImplementedException ();
/*
		GdipPropertyItem pi = new GdipPropertyItem ();
		GdipPropertyItem.MarshalTo (pi, propitem);
		unsafe {
			Status status = GDIPlus.GdipSetPropertyItem (nativeObject, &pi);
			
			GDIPlus.CheckStatus (status);
		}
*/
	}

	// properties	
	[Browsable (false)]
	public int Flags {
		get {
			int flags;
			
			Status status = GDIPlus.GdipGetImageFlags (nativeObject, out flags);			
			GDIPlus.CheckStatus (status);						
			return flags;			
		}
	}
	
	[Browsable (false)]
	public Guid[] FrameDimensionsList {
		get {
			uint found;
			Status status = GDIPlus.GdipImageGetFrameDimensionsCount (nativeObject, out found);
			GDIPlus.CheckStatus (status);
			Guid [] guid = new Guid [found];
			status = GDIPlus.GdipImageGetFrameDimensionsList (nativeObject, guid, found);
			GDIPlus.CheckStatus (status);  
			return guid;
		}
	}

	[DefaultValue (false)]
	[Browsable (false)]
	[DesignerSerializationVisibility (DesignerSerializationVisibility.Hidden)]
	public int Height {
		get {
			uint height;			
			Status status = GDIPlus.GdipGetImageHeight (nativeObject, out height);		
			GDIPlus.CheckStatus (status);			
			
			return (int)height;
		}
	}
	
	public float HorizontalResolution {
		get {
			float resolution;
			
			Status status = GDIPlus.GdipGetImageHorizontalResolution (nativeObject, out resolution);			
			GDIPlus.CheckStatus (status);			
			
			return resolution;
		}
	}
	
	[Browsable (false)]
	public ColorPalette Palette {
		get {							
			return retrieveGDIPalette();
		}
		set {
			storeGDIPalette(value);
		}
	}

	internal ColorPalette retrieveGDIPalette()
	{
		int bytes;
		ColorPalette ret = new ColorPalette ();

		Status st = GDIPlus.GdipGetImagePaletteSize (nativeObject, out bytes);
		GDIPlus.CheckStatus (st);
		IntPtr palette_data = Marshal.AllocHGlobal (bytes);
		try {
			st = GDIPlus.GdipGetImagePalette (nativeObject, palette_data, bytes);
			GDIPlus.CheckStatus (st);
			ret.setFromGDIPalette (palette_data);
			return ret;
		}

		finally {
			Marshal.FreeHGlobal (palette_data);
		}
	}

	internal void storeGDIPalette(ColorPalette palette)
	{
		if (palette == null) {
			throw new ArgumentNullException("palette");
		}
		IntPtr palette_data = palette.getGDIPalette();
		if (palette_data == IntPtr.Zero) {
			return;
		}

		try {
			Status st = GDIPlus.GdipSetImagePalette (nativeObject, palette_data);
			GDIPlus.CheckStatus (st);
		}

		finally {
			Marshal.FreeHGlobal(palette_data);
		}
	}

		
	public SizeF_ PhysicalDimension {
		get {
			float width,  height;
			Status status = GDIPlus.GdipGetImageDimension (nativeObject, out width, out height);		
			GDIPlus.CheckStatus (status);			
			
			return new SizeF_ (width, height);
		}
	}
	
	public PixelFormat PixelFormat {
		get {			
			PixelFormat pixFormat;				
			Status status = GDIPlus.GdipGetImagePixelFormat (nativeObject, out pixFormat);		
			GDIPlus.CheckStatus (status);			
			
			return pixFormat;
		}		
	}
	
	[Browsable (false)]
	public int[] PropertyIdList {
		get {
			uint propNumbers;
			
			Status status = GDIPlus.GdipGetPropertyCount (nativeObject, 
									out propNumbers);			
			GDIPlus.CheckStatus (status);
			
			int [] idList = new int [propNumbers];
			status = GDIPlus.GdipGetPropertyIdList (nativeObject, 
								propNumbers, idList);
			GDIPlus.CheckStatus (status);
			
			return idList;
		}
	}
	
	[Browsable (false)]
	public PropertyItem[] PropertyItems {
		get {
			int propNums, propsSize, propSize;
			IntPtr properties, propPtr;
			PropertyItem[] items;
			GdipPropertyItem gdipProperty = new GdipPropertyItem ();
			Status status;
			
			status = GDIPlus.GdipGetPropertySize (nativeObject, out propsSize, out propNums);
			GDIPlus.CheckStatus (status);

			items =  new PropertyItem [propNums];
			
			if (propNums == 0)
				return items;			
					
			/* Get PropertyItem list*/
			properties = Marshal.AllocHGlobal (propsSize * propNums);
			try {
				status = GDIPlus.GdipGetAllPropertyItems (nativeObject, propsSize, 
								propNums, properties);
				GDIPlus.CheckStatus (status);

				propSize = Marshal.SizeOf (gdipProperty);			
				propPtr = properties;
			
				for (int i = 0; i < propNums; i++, propPtr = new IntPtr (propPtr.ToInt64 () + propSize)) {
					gdipProperty = (GdipPropertyItem) Marshal.PtrToStructure 
						(propPtr, typeof (GdipPropertyItem));						
					items [i] = new PropertyItem ();
					GdipPropertyItem.MarshalTo (gdipProperty, items [i]);								
				}
			}
			finally {
				Marshal.FreeHGlobal (properties);
			}
			return items;
		}
	}

	public ImageFormat RawFormat {
		get {
			Guid guid;
			Status st = GDIPlus.GdipGetImageRawFormat (nativeObject, out guid);
			
			GDIPlus.CheckStatus (st);
			return new ImageFormat (guid);			
		}
	}
	
	public Size_ Size {
		get {
			return new Size_(Width, Height);
		}
	}

#if NET_2_0
	[DefaultValue (null)]
	[LocalizableAttribute(false)] 
	[BindableAttribute(true)] 	
	[TypeConverter (typeof (StringConverter))]
	public object Tag { 
		get { return tag; }
		set { tag = value; }
	}
#endif	
	public float VerticalResolution {
		get {
			float resolution;
			
			Status status = GDIPlus.GdipGetImageVerticalResolution (nativeObject, out resolution);
			GDIPlus.CheckStatus (status);

			return resolution;
		}
	}

	[DefaultValue (false)]
	[Browsable (false)]
	[DesignerSerializationVisibility (DesignerSerializationVisibility.Hidden)]
	public int Width {
		get {
			uint width;			
			Status status = GDIPlus.GdipGetImageWidth (nativeObject, out width);		
			GDIPlus.CheckStatus (status);			
			
			return (int)width;
		}
	}
	
	internal IntPtr NativeObject{
		get{
			return nativeObject;
		}
		set	{
			nativeObject = value;
		}
	}
	
	public void Dispose ()
	{
		Dispose (true);
		GC.SuppressFinalize (this);
	}

	~Image ()
	{
		Dispose (false);
	}

	protected virtual void Dispose (bool disposing)
	{
		if (GDIPlus.GdiPlusToken != 0 && nativeObject != IntPtr.Zero) {
			Status status = GDIPlus.GdipDisposeImage (nativeObject);
			// dispose the stream (set under Win32 only if SD owns the stream) and ...
			if (stream != null) {
				stream.Dispose();
				stream = null;
			}
			// ... set nativeObject to null before (possibly) throwing an exception
			nativeObject = IntPtr.Zero;
			GDIPlus.CheckStatus (status);		
		}
	}
	
	public object Clone ()
	{
		if (GDIPlus.RunningOnWindows () && stream != null)
			return CloneFromStream ();

		IntPtr newimage = IntPtr.Zero;
		Status status = GDIPlus.GdipCloneImage (NativeObject, out newimage);
		GDIPlus.CheckStatus (status);

		if (this is Bitmap)
			return new Bitmap (newimage);
		else
			return new Metafile (newimage);
	}

	// On win32, when cloning images that were originally created from a stream, we need to
	// clone both the image and the stream to make sure the gc doesn't kill it
	// (when using MS GDI+ and IStream we must ensure the stream stays alive for all the life of the Image)
	object CloneFromStream ()
	{
		byte[] bytes = new byte [stream.Length];
		MemoryStream ms = new MemoryStream (bytes);
		int count = (stream.Length < 4096 ? (int) stream.Length : 4096);
		byte[] buffer = new byte[count];
		stream.Position = 0;
		do {
			count = stream.Read (buffer, 0, count);
			ms.Write (buffer, 0, count);
		} while (count == 4096);

		IntPtr newimage = IntPtr.Zero;
		newimage = InitFromStream (ms);

		if (this is Bitmap)
			return new Bitmap (newimage, ms);
		else
			return new Metafile (newimage, ms);
	}

}

}
