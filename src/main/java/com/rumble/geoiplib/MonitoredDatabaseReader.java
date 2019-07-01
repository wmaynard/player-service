package com.rumble.geoiplib;

import com.maxmind.geoip2.DatabaseReader;
import com.maxmind.geoip2.GeoIp2Provider;
import com.maxmind.geoip2.exception.GeoIp2Exception;
import com.maxmind.geoip2.model.CityResponse;
import com.maxmind.geoip2.model.CountryResponse;
import org.apache.commons.vfs2.FileChangeEvent;
import org.apache.commons.vfs2.FileListener;

import java.io.File;
import java.io.FileNotFoundException;
import java.io.IOException;
import java.net.InetAddress;
import java.util.concurrent.locks.Lock;
import java.util.concurrent.locks.ReadWriteLock;
import java.util.concurrent.locks.ReentrantReadWriteLock;

/**
 * This is a wrapper for the MaxMind APIs
 * @see com.maxmind.geoip2.GeoIp2Provider
 * This class will allows you to read MaxMind binary databases and obtain Geolocation information.
 * The key difference is that it monitors the file it is reading from and updates itself if needed.
 *
 * @author Sherman Pay
 * @version 1.0  6/30/14
 */
public class MonitoredDatabaseReader implements GeoIp2Provider {
    private DatabaseReader databaseReader;
    private File databaseFile;
    private FileWatcher watcher;

    private final ReadWriteLock lock;
    private final Lock writeLock;
    private final Lock readLock;

    /**
     * Create a MonitoredDatabaseReader that reads the database file passed in
     * @param databaseFile to be read by this
     */
    public MonitoredDatabaseReader(File databaseFile) {
        lock = new ReentrantReadWriteLock();
        writeLock = lock.writeLock();
        readLock = lock.readLock();
        try {
            if (!databaseFile.exists()) {
                throw new FileNotFoundException("Database file " + databaseFile.getAbsolutePath() +
                        "does not exist!");
            }
            databaseReader = new DatabaseReader.Builder(databaseFile).build();
        } catch (IOException e) {
            System.err.println("Error reading Database File " + e.getMessage());
        } finally {
            this.databaseFile = databaseFile;
            this.watcher = new FileWatcher();
            watcher.watch(databaseFile.getAbsoluteFile(), new DatabaseFileListener());
        }
    }

    /**
     * Reassign the database reader to the file passed in.
     * It will acquire a WriteLock, disallowing other threads to read/write the database.
     * @param databaseFile to be read by this
     */
    public void reload(File databaseFile) {
        writeLock.lock();
        try {
            databaseReader = new DatabaseReader.Builder(databaseFile).build();
        } catch(IOException e) {
            System.err.println("Error reading Database File " + e.getMessage());
        } finally {
            this.databaseFile = databaseFile;
            this.watcher = new FileWatcher();
            watcher.watch(databaseFile.getAbsoluteFile(), new DatabaseFileListener());
            writeLock.unlock();
        }
    }

    /**
     * Gets the delay in milliseconds.
     * @return long representing the delay
     */
    public long getDelay() {
        return this.watcher.getDelay();
    }

    /**
     * Set the delay in milliseconds.
     * @param delay long representing the delay
     */
    public void setDelay(long delay) {
        this.watcher.setDelay(delay);
    }

    /**
     * Obtain a CountryResponse object given an InetAddress.
     * @see com.maxmind.geoip2.model.CountryResponse
     * @param ipAddress InetAddress to find the CountryResponse
     *
     * @throws java.io.IOException if there is an IO error.
     * @throws com.maxmind.geoip2.exception.GeoIp2Exception if there is an error looking up ip
     * @return CountryResponse object
     */
    @Override
    public CountryResponse country(InetAddress ipAddress) throws IOException, GeoIp2Exception {
        readLock.lock();
        try {
            if (databaseReader == null) {
                System.err.println("Database is NULL");
                return null;
            }
            return databaseReader.country(ipAddress);
        } finally {
            readLock.unlock();
        }
    }

    /**
     * Obtain a CityResponse object given an InetAddress.
     * @see com.maxmind.geoip2.model.CityResponse
     * @param ipAddress InetAddress to find the CityResponse
     * @throws java.io.IOException if there is an IO error.
     * @throws com.maxmind.geoip2.exception.GeoIp2Exception if there is an error looking up ip
     * @return CityResponse object
     */
    @Override
    public CityResponse city(InetAddress ipAddress) throws IOException, GeoIp2Exception {
        try {
            readLock.lock();
            return databaseReader.city(ipAddress);
        } finally {
            readLock.unlock();
        }
    }

    /**
     * Class that will be notified when the Database file is created, deleted or changed.
     */
    private class DatabaseFileListener implements FileListener {
        @Override
        public void fileCreated(FileChangeEvent event) throws Exception {
            System.out.println("File Created");
            reload(new File(event.getFile().getName().getPath()));
        }

        @Override
        public void fileDeleted(FileChangeEvent event) throws Exception {
            // Ignore, databaseReader will still work.
        }

        @Override
        public void fileChanged(FileChangeEvent event) throws Exception {
            System.out.println("File changed");
            reload(new File(event.getFile().getName().getPath()));
        }
    }
}
