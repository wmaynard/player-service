package com.rumble.geoiplib;

import org.apache.commons.vfs2.*;
import org.apache.commons.vfs2.impl.DefaultFileMonitor;

import java.io.File;

/**
 * This class allows you to watch a file and execute appropriate actions via a FileListener
 * @author Sherman Pay
 * @version 6/30/14.
 */
class FileWatcher {
    /** The default delay period in milliseconds*/
    public static final int DELAY = 60000;

    /** delay period between watches in milliseconds */
    private long delay;
    /**
     * Used for monitoring files
     * @see org.apache.commons.vfs2.impl.DefaultFileMonitor
     */
    private DefaultFileMonitor monitor;

    /** Create a FileWatcher with default delay as DELAY */
    public FileWatcher() {
        delay = DELAY;
    }

    /**
     * Create a FileWatcher with a specifed delay
     * @param delay a long specifying the delay in milliseconds
     */
    public FileWatcher(long delay) {
        this.delay = delay;
    }

    /**
     * Gets the delay of the FileWatcher in milliseconds
     * @return long representing the delay
     */
    public long getDelay() {
        return delay;
    }

    /**
     * Sets the delay of the FileWatcher in milliseconds
     * @param delay a long representing the delay
     */
    public void setDelay(long delay) {
        this.delay = delay;
        if (monitor != null) {
            monitor.setDelay(delay);
        }
    }

    /**
     * Watch a specific file, and attach a FileListener that will be notified when the file is created, modified, deleted.
     * @param file File to watch
     * @param listener FileListenter
     */
    public void watch(File file, FileListener listener) {
        watch(file.getPath(), listener);
    }

    /**
     * Watch a specific file at specified path
     * @param pathToFile String representing path to file
     * @param listener FileListener
     */
    public void watch(String pathToFile, FileListener listener) {
        try {
            FileObject file = VFS.getManager().resolveFile(pathToFile);
            monitor = new DefaultFileMonitor(listener);
            monitor.setDelay(this.delay);
            monitor.addFile(file);
            monitor.start();
        } catch (FileSystemException e) {
            System.err.println("Error resolving " + pathToFile + e.getMessage());
        }
    }

    /**
     * Stops this monitor
     * @throws java.lang.IllegalStateException if this monitor is not watching any file
     */
    public void stop() {
        if (monitor != null) {
            monitor.stop();
        } else {
            throw new IllegalStateException("Monitor is null, nothing to stop");
        }
    }
}
