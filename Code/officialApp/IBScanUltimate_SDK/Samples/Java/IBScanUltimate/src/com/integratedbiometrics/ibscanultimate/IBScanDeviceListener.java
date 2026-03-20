/**
****************************************************************************************************
* @file
*       IBScanDeviceListener.java
*
* @brief
*       Java wrapper for IBScanUltimate library
*
* @author
*       Integrated Biometrics, LLC
*
* @copyright
*       Copyright (c) Integrated Biometrics, 2009-2023 \n
*       http://www.integratedbiometrics.com
*
* @page page_API_Revision_History_IBScanDeviceListener IBScanDeviceListener.java 
* @section section_IBScanDeviceListener IBScanDeviceListener.java
* @li @par  2015/12/11
*						Added method for detecting the key button of device was pressed.
*
* @li @par  2013/10/18
*						Added method for extended result information.
*
* @li @par  2013/03/01
*						First version.
****************************************************************************************************
*/
package com.integratedbiometrics.ibscanultimate;

import com.integratedbiometrics.ibscanultimate.IBScanDevice.FingerCountState;
import com.integratedbiometrics.ibscanultimate.IBScanDevice.FingerQualityState;
import com.integratedbiometrics.ibscanultimate.IBScanDevice.ImageData;
import com.integratedbiometrics.ibscanultimate.IBScanDevice.ImageType;
import com.integratedbiometrics.ibscanultimate.IBScanDevice.PlatenState;
import com.integratedbiometrics.ibscanultimate.IBScanDevice.SegmentPosition;

/**
****************************************************************************************************
* @defgroup group_API_Main_Interface_IBScanDeviceListener Interface - IBScanDeviceListener
* @{
****************************************************************************************************
*/

/**
* @brief	Listener for scan events on a IBScanDevice. This listener should be registered by an
* 			application using the setScanDeviceListener(IBScanDeviceListener) method.
* 			<p>
*			Most of these events occur after beginCaptureImage() has been called.  If fingers 
*			are touching the platen when the capture is begun, deviceImagePreviewAvailable()
*			will be called immediately and again once no fingers are touching.  Periodically, until a final 
*			image is achieved, deviceImagePreviewAvailable() will return the current scanner 
*			image.  Changes in the quantity and quality of finger presses will result in 
*			deviceFingerCountChanged() or deviceFingerQualityChanged() calls.  If 
*			the selected scan type is a rolled finger scan, then deviceAcquisitionBegun()
*			will be called when a flat finger scan has been acquired and the user should begin rolling his 
*			or her finger to the left; when the left-roll is complete, deviceAcquisitionCompleted()
*			will be called, and the user should begin rolling back toward the right.  When a quality scan 
*			with the correct number of fingers (and a full finger roller, if applicable) is available or 
*			captureImageManually() is called, deviceImageResultAvailable() or
*			deviceImageResultExtendedAvailable() will supply a final scan image to the application.
*/
public interface IBScanDeviceListener
{
    /**
    * @brief	Communication break notification.  This method is called when communication with the device
    * 			is broken while a capture is in progress.
    * 
    * @param	device  device with which communication has been broken
    */
    public void deviceCommunicationBroken(IBScanDevice device);

    /**
    * @brief	Image preview available notification.
    * 
    * @param	device  device for which preview image is available
    * @param	image   preview image
	* @throws	IBScanException
    */
    public void deviceImagePreviewAvailable(IBScanDevice device, ImageData image) throws IBScanException;

    /**
    * @brief	Finger count change notification.
    * 
    * @param	device       device for which finger count has changed
    * @param	fingerState  state of finger count
    */
    public void deviceFingerCountChanged(IBScanDevice device, FingerCountState fingerState);

    /**
    * @brief	Finger quality change notification.
    * 
    * @param	device           device for which finger quality has changed
    * @param	fingerQualities  array of qualities for fingers
    */
    public void deviceFingerQualityChanged(IBScanDevice device, FingerQualityState[] fingerQualities);

    /**
    * @brief	Device roll acquisition begun notification.  If an image type of ROLL_SINGLE_FINGER
    * 			is being captured, this method will be called when a flat-finger scan has been acquired and
    * 			the user should begin rolling his or her finger to the left.
    * 
    * @param	device     device for which acquisition has begun
    * @param	imageType  type of image
    */
    public void deviceAcquisitionBegun(IBScanDevice device, ImageType imageType);

    /**
    * @brief	Device roll acquisition complete notification.  If an image type of ROLL_SINGLE_FINGER
    * 			is being captured, this method will be called when the left-roll has been completed and the
    * 			user should begin roller his or her finger to the left to capture the right side of the 
    * 			finger.
    * 
    * @param	device     device for which acquisition has completed
    * @param	imageType  type of image
    */
    public void deviceAcquisitionCompleted(IBScanDevice device, ImageType imageType);

    /**
    * @brief	Result image available notification.
    * 
    * @param	device           device for which result image is available
    * @param	image            result image data
    * @param	imageType        type of image
    * @param	splitImageArray  array of split result image data
    */
    public void deviceImageResultAvailable(IBScanDevice device, ImageData image,
    		ImageType imageType, ImageData[] splitImageArray);

    /**
    * @brief	Result extended image available notification.
    * 
    * @param	device                device for which result image is available
    * @param	imageStatus           status from result image acquisition
    * @param	image                 result image data
    * @param	imageType             type of image
    * @param	detectedFingerCount   detected finger count
    * @param	segmentImageArray     array of segment result image data
    * @param	segmentPositionArray  array of segment position data
    */
    public void deviceImageResultExtendedAvailable(IBScanDevice device, IBScanException imageStatus,
    		ImageData image, ImageType imageType, int detectedFingerCount, ImageData[] segmentImageArray,
    		SegmentPosition[] segmentPositionArray);

    /**
    * @brief	Platen state changed notification.  If fingers are touching the platen when a capture is 
    * 			begun, this method will be called immediately and once again when no fingers are touching.
    * 			Subsequent state changes from touches will not be notified.
    * 
    * @param	device       device for which platen state has changed
    * @param	platenState  new platen state
    */
    public void devicePlatenStateChanged(IBScanDevice device, PlatenState platenState);

    /**
    * @brief	Warning message notification.
    * 
    * @param	device   device for which warning was received
    * @param	warning  warning received from device
    */
    public void deviceWarningReceived(IBScanDevice device, IBScanException warning);
    
    /**
    * @brief	Key button notification.
    * 
    * @param	device             device for which key button was pressed
    * @param	pressedKeyButtons  The key button index which is pressed
    */
    public void devicePressedKeyButtons(IBScanDevice device, int pressedKeyButtons);
}
/**
****************************************************************************************************
* @}
****************************************************************************************************
*/